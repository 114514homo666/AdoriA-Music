using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CloudMusicFloat
{
    /// <summary>
    /// 轻量 WPF 动画工具：只处理 double 依赖属性，全部调用必须在 UI 线程。
    /// 状态用 ConditionalWeakTable 保存，不长期持有控件，且每次调用都带版本号(令牌)，
    /// 旧动画的 Completed 回调只要发现令牌过期就直接丢弃，绝不会覆盖新状态。
    /// </summary>
    public static class Motion
    {
        private const double Epsilon = 0.0001;
        private const double FadeOutMs = 100.0;
        private const double FadeInMs = 160.0;

        private static readonly ConditionalWeakTable<DependencyObject, State> States =
            new ConditionalWeakTable<DependencyObject, State>();

        private static readonly ConditionalWeakTable<DependencyObject, State>.CreateValueCallback StateFactory =
            CreateState;

        // ------------------------------------------------------------------
        // 1. 通用 double 属性补间
        // ------------------------------------------------------------------
        public static void To(DependencyObject target, DependencyProperty property, double value, int milliseconds = 240)
        {
            if (target == null || property == null)
            {
                return;
            }

            if (milliseconds < 0)
            {
                milliseconds = 0;
            }

            // 先自增令牌：任何在此之前的动画，其 Completed 都会因令牌过期而被忽略。
            long token = NextToken(target, property);

            if (property.PropertyType != typeof(double))
            {
                TrySetValue(target, property, value);
                return;
            }

            Freezable freezable = target as Freezable;
            if (freezable != null && freezable.IsFrozen)
            {
                return; // 冻结对象无法动画，也无法写值
            }

            if (!(target is UIElement) && !(target is Animatable))
            {
                TrySetValue(target, property, value);
                return;
            }

            double from;
            object raw = target.GetValue(property);
            if (raw is double)
            {
                from = (double)raw;
            }
            else
            {
                TrySetValue(target, property, value);
                return;
            }

            // 无需动画的情形：直接落地基础值并卸下旧动画
            if (milliseconds == 0 || double.IsNaN(from) || double.IsInfinity(from) || AreClose(from, value))
            {
                TrySetValue(target, property, value);
                BeginAnimation(target, property, null);
                return;
            }

            DoubleAnimation animation = new DoubleAnimation(
                from, value, new Duration(TimeSpan.FromMilliseconds(milliseconds)));
            animation.FillBehavior = FillBehavior.HoldEnd;
            animation.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            animation.Completed += delegate(object sender, EventArgs e)
            {
                State state = GetState(target);
                long current;
                if (!state.Tokens.TryGetValue(property, out current) || current != token)
                {
                    return; // 已有更新的调用，旧回调不得覆盖新状态
                }

                // 先把最终基础值落地，再移除动画（同一 UI 帧内完成，不会闪回）
                TrySetValue(target, property, value);
                BeginAnimation(target, property, null);
            };

            BeginAnimation(target, property, animation);
        }

        // ------------------------------------------------------------------
        // 2. 文本切换：100ms 淡出 -> 换字 -> 160ms 淡入，无 async void
        // ------------------------------------------------------------------
        public static void Text(TextBlock block, string value)
        {
            if (block == null)
            {
                return;
            }

            if (value == null)
            {
                value = string.Empty;
            }

            State state = GetState(block);

            if (!state.TextBusy)
            {
                if (string.Equals(block.Text, value, StringComparison.Ordinal))
                {
                    return; // 内容不变就不做事
                }

                state.RestoreOpacity = block.Opacity;
            }
            else if (string.Equals(state.TargetText, value, StringComparison.Ordinal))
            {
                return; // 已经在切到同一段文字的路上
            }

            state.TextBusy = true;
            state.TargetText = value;
            state.TextVersion = state.TextVersion + 1;
            long token = state.TextVersion;

            double from = block.Opacity;
            DoubleAnimation fadeOut = new DoubleAnimation(
                from, 0.0, new Duration(TimeSpan.FromMilliseconds(FadeOutMs)));
            fadeOut.FillBehavior = FillBehavior.HoldEnd;
            fadeOut.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            fadeOut.Completed += delegate(object sender, EventArgs e)
            {
                State s = GetState(block);
                if (s.TextVersion != token)
                {
                    return; // 期间又来了新文本，这次作废
                }

                block.Text = s.TargetText;
                StartFadeIn(block, s, token);
            };

            BeginAnimation(block, UIElement.OpacityProperty, fadeOut);
        }

        private static void StartFadeIn(TextBlock block, State state, long token)
        {
            double target = state.RestoreOpacity;

            DoubleAnimation fadeIn = new DoubleAnimation(
                0.0, target, new Duration(TimeSpan.FromMilliseconds(FadeInMs)));
            fadeIn.FillBehavior = FillBehavior.HoldEnd;
            fadeIn.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            fadeIn.Completed += delegate(object sender, EventArgs e)
            {
                State s = GetState(block);
                if (s.TextVersion != token)
                {
                    return;
                }

                TrySetValue(block, UIElement.OpacityProperty, target);
                BeginAnimation(block, UIElement.OpacityProperty, null);
                block.Text = s.TargetText;
                s.TextBusy = false;
            };

            BeginAnimation(block, UIElement.OpacityProperty, fadeIn);
        }

        // ------------------------------------------------------------------
        // 3. 透明度淡入淡出
        // ------------------------------------------------------------------
        public static void Fade(UIElement element, double opacity, int milliseconds = 200)
        {
            if (element == null)
            {
                return;
            }

            To(element, UIElement.OpacityProperty, opacity, milliseconds);
        }

        // ------------------------------------------------------------------
        // 内部工具
        // ------------------------------------------------------------------
        private static State CreateState(DependencyObject key)
        {
            return new State();
        }

        private static State GetState(DependencyObject key)
        {
            return States.GetValue(key, StateFactory);
        }

        private static long NextToken(DependencyObject target, DependencyProperty property)
        {
            State state = GetState(target);
            long token;
            if (!state.Tokens.TryGetValue(property, out token))
            {
                token = 0;
            }

            token = token + 1;
            state.Tokens[property] = token;
            return token;
        }

        private static void BeginAnimation(DependencyObject target, DependencyProperty property, AnimationTimeline animation)
        {
            UIElement element = target as UIElement;
            if (element != null)
            {
                element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
                return;
            }

            Animatable animatable = target as Animatable;
            if (animatable != null)
            {
                animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
            }
        }

        private static void TrySetValue(DependencyObject target, DependencyProperty property, double value)
        {
            try
            {
                target.SetValue(property, value);
            }
            catch (InvalidOperationException)
            {
                // 只读依赖属性（如 ActualWidth）无法写基础值，忽略
            }
            catch (ArgumentException)
            {
            }
        }

        private static bool AreClose(double a, double b)
        {
            if (a == b)
            {
                return true;
            }

            if (double.IsNaN(a) || double.IsNaN(b) || double.IsInfinity(a) || double.IsInfinity(b))
            {
                return false;
            }

            return Math.Abs(a - b) < Epsilon;
        }

        private sealed class State
        {
            public readonly Dictionary<DependencyProperty, long> Tokens;
            public long TextVersion;
            public bool TextBusy;
            public string TargetText;
            public double RestoreOpacity;

            public State()
            {
                Tokens = new Dictionary<DependencyProperty, long>();
                TextVersion = 0;
                TextBusy = false;
                TargetText = string.Empty;
                RestoreOpacity = 1.0;
            }
        }
    }
}
