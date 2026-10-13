// SystemMediaReader.cs
// 通过反射投影访问系统自带 WinRT 类型 (SMTC)，不依赖 Windows SDK 的 winmd 编译引用。
// 目标：.NET Framework 4.x / C# 5 / 内置 csc / 无 NuGet。
// 编译：csc /target:library SystemMediaReader.cs   （本文件只引用 mscorlib，无需其它 /r）
// 说明：所有 WinRT 类型与成员均用显式 Type.GetType(...) + MethodInfo/PropertyInfo 解析，
//       不依赖 __ComObject.GetType() 的成员列表；IAsyncOperation<T> 统一走
//       System.WindowsRuntimeSystemExtensions.AsTask<T> 的反射；所有同步等待受同一 2500ms 预算约束。

using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CloudMusicFloat
{
    /// <summary>一次读取得到的媒体快照。</summary>
    public sealed class MediaSnapshot
    {
        public string AppId = "", Title = "", Artist = "", Album = "";
        public double Position, Duration;
        public bool Playing;
        public byte[] Cover;
    }

    public sealed class SystemMediaReader : IDisposable
    {
        // ---- 常量 --------------------------------------------------------------
        private const int TimeOutMs = 2500;          // 每次 Read/Toggle 的同步等待总预算
        private const string WinRtTail = ", Windows.Media.Control, ContentType=WindowsRuntime";

        private const string TnManager  = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager" + WinRtTail;
        private const string TnSession  = "Windows.Media.Control.GlobalSystemMediaTransportControlsSession" + WinRtTail;
        private const string TnMedia    = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties" + WinRtTail;
        private const string TnTimeline = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionTimelineProperties" + WinRtTail;
        private const string TnPlayback = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackInfo" + WinRtTail;
        private const string TnStatus   = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus" + WinRtTail;

        // ---- 状态 --------------------------------------------------------------
        private readonly object _gate = new object();   // 所有调用串行
        private bool _disposed;

        private object _manager;                        // 缓存的 session manager（失效时重连）

        private Type _managerType, _sessionType, _mediaType, _timelineType, _playbackType, _statusType;
        private MethodInfo _asTask;
        private MethodInfo _mgrRequest, _mgrGetSessions;
        private MethodInfo _sessTryMedia, _sessTryToggle;
        private PropertyInfo _sessAppId;
        private MethodInfo _sessPlayback, _sessTimeline;
        private string _coverKey = "";
        private byte[] _cover;
        private DateTime _coverRetry = DateTime.MinValue;
        private PropertyInfo _mpTitle, _mpArtist, _mpAlbum, _mpAlbumArtist;
        private PropertyInfo _tlStart, _tlEnd, _tlPos, _tlLast, _tlMaxSeek;
        private PropertyInfo _pbStatus;
        private object _playingValue;

        public string Error { get; private set; }

        public SystemMediaReader()
        {
            Error = "";
        }

        // ======================================================================
        // 公开接口
        // ======================================================================
        public MediaSnapshot Read(string appHint)
        {
            MediaSnapshot snap = new MediaSnapshot();
            lock (_gate)
            {
                Error = "";
                if (_disposed) { Error = "对象已释放"; return snap; }
                if (string.IsNullOrEmpty(appHint)) { Error = "未指定播放器"; return snap; }

                Budget budget = new Budget(TimeOutMs);
                try
                {
                    if (!EnsureTypes()) return snap;

                    object session = GetSession(appHint, budget);
                    if (session == null)
                    {
                        if (string.IsNullOrEmpty(Error)) Error = ShortName(appHint) + "未在播放";
                        return snap;
                    }

                    snap.AppId = GetString(GetProperty(session, _sessAppId));

                    object pb = _sessPlayback.Invoke(session, null);
                    object status = (pb == null) ? null : GetProperty(pb, _pbStatus);
                    snap.Playing = IsPlaying(status);

                    string err = null;
                    object op = CallMethod(session, _sessTryMedia, null, out err);
                    object mp = null;
                    if (op != null && WaitOp(op, _mediaType, budget, out mp, out err) && mp != null)
                    {
                        snap.Title = GetString(GetProperty(mp, _mpTitle));
                        snap.Artist = GetString(GetProperty(mp, _mpArtist));
                        if (snap.Artist.Length == 0) snap.Artist = GetString(GetProperty(mp, _mpAlbumArtist));
                        snap.Album = GetString(GetProperty(mp, _mpAlbum));
                        string coverKey = snap.AppId + "|" + snap.Title + "|" + snap.Artist + "|" + snap.Album;
                        if (coverKey != _coverKey) { _coverKey = coverKey; _cover = null; _coverRetry = DateTime.MinValue; }
                        if (_cover == null && DateTime.UtcNow >= _coverRetry) { _coverRetry = DateTime.UtcNow.AddSeconds(3); _cover = ReadCover(mp, budget); }
                        snap.Cover = _cover;
                    }
                    else if (!string.IsNullOrEmpty(err))
                    {
                        Error = err;                       // 软错误：快照仍带 AppId/Playing/时间线
                    }
                    else
                    {
                        Error = "读取媒体信息失败";
                    }
                    // Media properties are asynchronous. Read the timeline afterwards,
                    // so a new title is not paired with a pre-await duration snapshot.
                    pb = _sessPlayback.Invoke(session, null);
                    status = (pb == null) ? null : GetProperty(pb, _pbStatus);
                    snap.Playing = IsPlaying(status);
                    FillTimeline(snap, _sessTimeline.Invoke(session, null), snap.Playing);

                }
                catch (Exception)
                {
                    Error = "读取失败";
                }
            }
            return snap;
        }

        public bool Toggle(string appHint)
        {
            lock (_gate)
            {
                Error = "";
                if (_disposed) { Error = "对象已释放"; return false; }
                if (string.IsNullOrEmpty(appHint)) { Error = "未指定播放器"; return false; }

                Budget budget = new Budget(TimeOutMs);
                try
                {
                    if (!EnsureTypes()) return false;

                    object session = GetSession(appHint, budget);
                    if (session == null)
                    {
                        if (string.IsNullOrEmpty(Error)) Error = ShortName(appHint) + "未在播放";
                        return false;
                    }

                    string err = null;
                    object op = CallMethod(session, _sessTryToggle, null, out err);
                    if (op == null) { Error = string.IsNullOrEmpty(err) ? "调用失败" : err; return false; }

                    object res = null;
                    if (!WaitOp(op, typeof(bool), budget, out res, out err))
                    {
                        Error = string.IsNullOrEmpty(err) ? "调用失败" : err;
                        return false;
                    }

                    bool ok = (res is bool) && (bool)res;
                    if (!ok) Error = "播放器拒绝了该操作";
                    return ok;
                }
                catch (Exception)
                {
                    Error = "操作失败";
                    return false;
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                ReleaseManager();
                _asTask = null;
                _mgrRequest = null;
                _mgrGetSessions = null;
                _sessTryMedia = null;
                _sessTryToggle = null;
                _sessionType = null;   // 使 EnsureTypes 不再走缓存
                _managerType = null;
            }
        }

        // ======================================================================
        // 类型 / 成员解析（全部走显式 Type.GetType，不碰 __ComObject.GetType()）
        // ======================================================================
        private bool EnsureTypes()
        {
            if (_managerType != null && _sessionType != null && _asTask != null && _mgrRequest != null)
                return true;

            try
            {
                Type mgr = Type.GetType(TnManager, false);
                Type sess = Type.GetType(TnSession, false);
                if (mgr == null || sess == null) { Error = "系统媒体服务不可用"; return false; }

                Type media = Type.GetType(TnMedia, false);
                Type line = Type.GetType(TnTimeline, false);
                Type play = Type.GetType(TnPlayback, false);
                Type stat = Type.GetType(TnStatus, false);

                MethodInfo asTask = FindAsTask();
                if (asTask == null) { Error = "缺少异步运行库"; return false; }

                MethodInfo request = FindMethod(mgr, "RequestAsync", 0, true);
                MethodInfo sessions = FindMethod(mgr, "GetSessions", 0, false);
                if (request == null || sessions == null) { Error = "系统媒体服务不可用"; return false; }

                MethodInfo tryMedia = FindMethod(sess, "TryGetMediaPropertiesAsync", 0, false);
                MethodInfo tryToggle = FindMethod(sess, "TryTogglePlayPauseAsync", 0, false);
                if (tryMedia == null || tryToggle == null) { Error = "系统媒体服务不可用"; return false; }

                PropertyInfo appId = FindProperty(sess, "SourceAppUserModelId");
                MethodInfo playback = FindMethod(sess, "GetPlaybackInfo", 0, false);
                MethodInfo timeline = FindMethod(sess, "GetTimelineProperties", 0, false);
                if (appId == null || playback == null || timeline == null) { Error = "系统媒体服务不可用"; return false; }

                PropertyInfo mpTitle = FindProperty(media, "Title");
                PropertyInfo mpArtist = FindProperty(media, "Artist");
                PropertyInfo mpAlbum = FindProperty(media, "AlbumTitle");
                PropertyInfo mpAlbumArtist = FindProperty(media, "AlbumArtist");

                PropertyInfo tlStart = FindProperty(line, "StartTime");
                PropertyInfo tlEnd = FindProperty(line, "EndTime");
                PropertyInfo tlPos = FindProperty(line, "Position");
                PropertyInfo tlLast = FindProperty(line, "LastUpdatedTime");
                PropertyInfo tlMaxSeek = FindProperty(line, "MaxSeekTime");

                PropertyInfo pbStatus = FindProperty(play, "PlaybackStatus");

                object playing = null;
                if (stat != null && stat.IsEnum)
                {
                    try { playing = Enum.Parse(stat, "Playing", false); }
                    catch (Exception) { playing = null; }
                }

                // 全部解析成功后再一次性赋值，避免半初始化状态
                _managerType = mgr;
                _sessionType = sess;
                _mediaType = media;
                _timelineType = line;
                _playbackType = play;
                _statusType = stat;
                _asTask = asTask;
                _mgrRequest = request;
                _mgrGetSessions = sessions;
                _sessTryMedia = tryMedia;
                _sessTryToggle = tryToggle;
                _sessAppId = appId;
                _sessPlayback = playback;
                _sessTimeline = timeline;
                _mpTitle = mpTitle;
                _mpArtist = mpArtist;
                _mpAlbum = mpAlbum;
                _mpAlbumArtist = mpAlbumArtist;
                _tlStart = tlStart;
                _tlEnd = tlEnd;
                _tlPos = tlPos;
                _tlLast = tlLast;
                _tlMaxSeek = tlMaxSeek;
                _pbStatus = pbStatus;
                _playingValue = playing;
                return true;
            }
            catch (Exception)
            {
                Error = "系统媒体服务不可用";
                return false;
            }
        }

        /// <summary>解析 System.WindowsRuntimeSystemExtensions（不写死编译期引用）。</summary>
        private static Type ResolveWrtExtensions()
        {
            string[] names = new string[]
            {
                "System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
                "System.WindowsRuntimeSystemExtensions, System.Runtime.WindowsRuntime"
            };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    Type t = Type.GetType(names[i], false);
                    if (t != null) return t;
                }
                catch (Exception) { }
            }
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Type t = null;
                    try { t = asms[i].GetType("System.WindowsRuntimeSystemExtensions", false, false); }
                    catch (Exception) { }
                    if (t != null) return t;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>找 AsTask&lt;TResult&gt;(IAsyncOperation&lt;TResult&gt;) 这一个泛型重载。</summary>
        private static MethodInfo FindAsTask()
        {
            Type t = ResolveWrtExtensions();
            if (t == null) return null;

            MethodInfo[] ms = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < ms.Length; i++)
            {
                MethodInfo m = ms[i];
                if (m.Name != "AsTask") continue;
                if (!m.IsGenericMethodDefinition) continue;
                if (m.GetGenericArguments().Length != 1) continue;

                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != 1) continue;

                if (ps[0].ParameterType.Name != "IAsyncOperation`1") continue;
                return m;
            }
            return null;
        }

        private static MethodInfo FindMethod(Type t, string name, int paramCount, bool isStatic)
        {
            if (t == null) return null;
            BindingFlags f = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            MethodInfo[] ms = t.GetMethods(f);
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i].Name != name) continue;
                if (ms[i].GetParameters().Length != paramCount) continue;
                return ms[i];
            }
            return null;
        }

        private static PropertyInfo FindProperty(Type t, string name)
        {
            if (t == null) return null;
            return t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        }

        // ======================================================================
        // 会话定位（严格按 SourceAppUserModelId 过滤，绝不退回其它播放器）
        // ======================================================================
        private object GetSession(string appHint, Budget budget)
        {
            object mgr = EnsureManager(budget);
            if (mgr == null) return null;

            string err = null;
            object sessions = CallMethod(mgr, _mgrGetSessions, null, out err);

            if (sessions == null)               // manager 失效 -> 重连一次
            {
                ReleaseManager();
                mgr = EnsureManager(budget);
                if (mgr == null) return null;
                sessions = CallMethod(mgr, _mgrGetSessions, null, out err);
                if (sessions == null)
                {
                    Error = string.IsNullOrEmpty(err) ? "读取会话失败" : err;
                    return null;
                }
            }

            return FindInList(sessions, appHint);
        }

        private object EnsureManager(Budget budget)
        {
            if (_manager != null) return _manager;
            if (_mgrRequest == null) { Error = "系统媒体服务不可用"; return null; }
            if (budget.Remaining <= 0) { Error = "调用超时"; return null; }

            string err = null;
            object op = CallMethod(null, _mgrRequest, null, out err);   // 静态方法
            if (op == null) { Error = string.IsNullOrEmpty(err) ? "系统媒体服务不可用" : err; return null; }

            object mgr = null;
            if (!WaitOp(op, _managerType, budget, out mgr, out err) || mgr == null)
            {
                Error = string.IsNullOrEmpty(err) ? "系统媒体服务不可用" : err;
                return null;
            }
            _manager = mgr;
            return _manager;
        }

        private void ReleaseManager()
        {
            object mgr = _manager;
            _manager = null;
            if (mgr != null)
            {
                try { Marshal.ReleaseComObject(mgr); }
                catch (Exception) { }
            }
        }

        /// <summary>主路径用 IEnumerable 枚举；不支持时退回 IReadOnlyList&lt;T&gt; 的 Count/Item。</summary>
        private object FindInList(object sessions, string appHint)
        {
            IEnumerable list = sessions as IEnumerable;
            if (list != null)
            {
                try
                {
                    foreach (object s in list)
                    {
                        if (s == null) continue;
                        if (MatchApp(GetString(GetProperty(s, _sessAppId)), appHint)) return s;
                    }
                    return null;
                }
                catch (Exception) { /* 落到后备路径 */ }
            }

            try
            {
                Type open = Type.GetType("System.Collections.Generic.IReadOnlyList`1, mscorlib", false);
                if (open == null || _sessionType == null) return null;

                Type closed = open.MakeGenericType(new Type[] { _sessionType });
                PropertyInfo count = closed.GetProperty("Count");
                PropertyInfo item = closed.GetProperty("Item");
                if (count == null || item == null) return null;

                int n = Convert.ToInt32(count.GetValue(sessions, null), CultureInfo.InvariantCulture);
                for (int i = 0; i < n; i++)
                {
                    object s = item.GetValue(sessions, new object[] { i });
                    if (s == null) continue;
                    if (MatchApp(GetString(GetProperty(s, _sessAppId)), appHint)) return s;
                }
            }
            catch (Exception) { }
            return null;
        }

        private static bool MatchApp(string aumid, string hint)
        {
            if (string.IsNullOrEmpty(aumid) || string.IsNullOrEmpty(hint)) return false;
            if (string.Equals(aumid, hint, StringComparison.OrdinalIgnoreCase)) return true;
            if (aumid.StartsWith(hint, StringComparison.OrdinalIgnoreCase)) return true;   // QQMusic.exe / cloudmusic.exe
            if (aumid.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0) return true; // com.tencent.qqmusic 之类
            return false;
        }

        // ======================================================================
        // 异步 -> Task 的反射桥
        // ======================================================================
        private bool WaitOp(object op, Type resultType, Budget budget, out object result, out string error)
        {
            result = null;
            error = null;

            if (op == null) { error = "调用失败"; return false; }
            if (_asTask == null || resultType == null) { error = "缺少异步运行库"; return false; }

            int left = budget.Remaining;
            if (left <= 0) { error = "调用超时"; return false; }

            try
            {
                MethodInfo generic = _asTask.MakeGenericMethod(new Type[] { resultType });
                object taskObj = generic.Invoke(null, new object[] { op });
                Task task = taskObj as Task;
                if (task == null) { error = "异步转换失败"; return false; }

                if (!task.Wait(left)) { error = "调用超时"; return false; }   // 超时即放弃，不阻塞外部
                if (task.IsFaulted || task.IsCanceled) { error = "调用失败"; return false; }

                PropertyInfo rp = taskObj.GetType().GetProperty("Result");
                if (rp != null) result = rp.GetValue(taskObj, null);
                return true;
            }
            catch (Exception)
            {
                error = "调用异常";
                return false;
            }
        }

        private static object CallMethod(object target, MethodInfo mi, object[] args, out string error)
        {
            error = null;
            if (mi == null) { error = "调用失败"; return null; }
            try
            {
                return mi.Invoke(target, args);
            }
            catch (Exception)
            {
                error = "调用失败";
                return null;
            }
        }

        // ======================================================================
        // 属性取值 / 时间线
        // ======================================================================
        private static object GetProperty(object target, PropertyInfo pi)
        {
            if (target == null || pi == null) return null;
            try { return pi.GetValue(target, null); }
            catch (Exception) { return null; }
        }

        private bool IsPlaying(object status)
        {
            if (status == null) return false;
            if (_playingValue != null && object.Equals(status, _playingValue)) return true;

            if (_statusType != null && _statusType.IsEnum)
            {
                try { return Convert.ToInt32(status, CultureInfo.InvariantCulture) == 4; }  // Playing
                catch (Exception) { }
            }
            string s = null;
            try { s = status.ToString(); }
            catch (Exception) { }
            return string.Equals(s, "Playing", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(s, "4", StringComparison.Ordinal);
        }

        private void FillTimeline(MediaSnapshot snap, object line, bool playing)
        {
            if (line == null) return;

            TimeSpan start = ToTimeSpan(GetProperty(line, _tlStart));
            TimeSpan end = ToTimeSpan(GetProperty(line, _tlEnd));
            TimeSpan pos = ToTimeSpan(GetProperty(line, _tlPos));
            TimeSpan maxSeek = ToTimeSpan(GetProperty(line, _tlMaxSeek));
            DateTimeOffset last = ToDto(GetProperty(line, _tlLast));

            double duration = (end - start).TotalSeconds;
            if (duration <= 0 && maxSeek.TotalSeconds > 0) duration = maxSeek.TotalSeconds;
            if (duration < 0) duration = 0;

            double p = pos.TotalSeconds;
            if (p < 0) p = 0;

            if (playing && last != default(DateTimeOffset))
            {
                double delta = (DateTimeOffset.UtcNow - last).TotalSeconds;
                if (delta > 0 && delta < 86400) p += delta;      // 播放中按 LastUpdatedTime 外推
            }

            if (duration > 0 && p > duration) p = duration;      // duration 钳制
            if (p < 0) p = 0;

            snap.Position = p;
            snap.Duration = duration;
        }

        private byte[] ReadCover(object media, Budget budget)
        {
            object stream = null, dataReader = null;
            try
            {
                object thumbnail = GetProperty(media, FindProperty(_mediaType, "Thumbnail"));
                if (thumbnail == null || budget.Remaining <= 0) return null;
                const string tail = ", Windows.Storage.Streams, ContentType=WindowsRuntime";
                Type referenceType = Type.GetType("Windows.Storage.Streams.IRandomAccessStreamReference" + tail, true);
                Type contentType = Type.GetType("Windows.Storage.Streams.IRandomAccessStreamWithContentType" + tail, true);
                Type randomType = Type.GetType("Windows.Storage.Streams.IRandomAccessStream" + tail, true);
                Type readerType = Type.GetType("Windows.Storage.Streams.DataReader" + tail, true);
                object operation = referenceType.GetMethod("OpenReadAsync").Invoke(thumbnail, null);
                string error;
                if (!WaitOp(operation, contentType, budget, out stream, out error) || stream == null) return null;
                ulong length = Convert.ToUInt64(randomType.GetProperty("Size").GetValue(stream, null));
                if (length == 0 || length > 4194304) return null;
                object input = randomType.GetMethod("GetInputStreamAt").Invoke(stream, new object[] { 0UL });
                dataReader = Activator.CreateInstance(readerType, new object[] { input });
                object loaded;
                operation = readerType.GetMethod("LoadAsync").Invoke(dataReader, new object[] { (uint)length });
                if (!WaitOp(operation, typeof(uint), budget, out loaded, out error) || Convert.ToUInt32(loaded) != length) return null;
                byte[] bytes = new byte[(int)length];
                readerType.GetMethod("ReadBytes").Invoke(dataReader, new object[] { bytes });
                return bytes;
            }
            catch { return null; }
            finally { var readerDisposable = dataReader as IDisposable; if (readerDisposable != null) readerDisposable.Dispose(); var streamDisposable = stream as IDisposable; if (streamDisposable != null) streamDisposable.Dispose(); }
        }

        private static TimeSpan ToTimeSpan(object o)
        {
            if (o is TimeSpan) return (TimeSpan)o;
            if (o == null) return TimeSpan.Zero;
            try { return TimeSpan.Parse(o.ToString(), CultureInfo.InvariantCulture); }
            catch (Exception) { return TimeSpan.Zero; }
        }

        private static DateTimeOffset ToDto(object o)
        {
            if (o is DateTimeOffset) return (DateTimeOffset)o;
            if (o == null) return default(DateTimeOffset);
            try { return DateTimeOffset.Parse(o.ToString(), CultureInfo.InvariantCulture); }
            catch (Exception) { return default(DateTimeOffset); }
        }

        private static string GetString(object o)
        {
            if (o == null) return "";
            string s = o as string;
            if (s != null) return s;
            try { return Convert.ToString(o, CultureInfo.InvariantCulture); }
            catch (Exception) { return ""; }
        }

        private static string ShortName(string hint)
        {
            if (string.IsNullOrEmpty(hint)) return "播放器";
            if (hint.IndexOf("qq", StringComparison.OrdinalIgnoreCase) >= 0) return "QQ音乐";
            if (hint.IndexOf("cloudmusic", StringComparison.OrdinalIgnoreCase) >= 0) return "网易云音乐";
            return hint;
        }

        // ======================================================================
        // 单次公开调用的 2500ms 同步等待预算
        // ======================================================================
        private sealed class Budget
        {
            private readonly int _total;
            private readonly DateTime _start;

            public Budget(int totalMs)
            {
                _total = totalMs;
                _start = DateTime.UtcNow;
            }

            public int Remaining
            {
                get
                {
                    int used = (int)(DateTime.UtcNow - _start).TotalMilliseconds;
                    int left = _total - used;
                    return left < 0 ? 0 : left;
                }
            }
        }
    }
}
