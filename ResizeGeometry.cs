using System;

namespace CloudMusicFloat
{
    public struct ResizeBounds
    {
        public double Left, Top, Width, Height;
    }

    public static class ResizeGeometry
    {
        static double Num(double v, double fallback)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;
        }

        public static ResizeBounds FromCorner(double left, double top, double width, double height,
            double dx, double dy, bool west, bool north,
            double minWidth, double minHeight, double maxWidth, double maxHeight)
        {
            left = Num(left, 0);
            top = Num(top, 0);
            width = Num(width, 0);
            height = Num(height, 0);
            dx = Num(dx, 0);
            dy = Num(dy, 0);

            double minW = Num(minWidth, 0);
            if (minW < 0) minW = 0;
            double minH = Num(minHeight, 0);
            if (minH < 0) minH = 0;

            double maxW = Num(maxWidth, double.MaxValue);
            if (maxW < minW) maxW = minW;
            double maxH = Num(maxHeight, double.MaxValue);
            if (maxH < minH) maxH = minH;

            double right = left + width;   // 右/下边在对边固定时保持不变
            double bottom = top + height;

            double w = west ? width - dx : width + dx;
            double h = north ? height - dy : height + dy;

            if (double.IsNaN(w) || w < minW) w = minW;
            if (w > maxW) w = maxW;
            if (double.IsNaN(h) || h < minH) h = minH;
            if (h > maxH) h = maxH;

            ResizeBounds b;
            b.Width = w;
            b.Height = h;
            b.Left = west ? right - w : left;
            b.Top = north ? bottom - h : top;
            return b;
        }

        // 断言建议（输入 -> 期望 Left/Top/Width/Height）：
        // 1. 右下角 SE: (100,100,300,200) dx=50 dy=40 west=false north=false min=240x64 max=∞
        //    -> 100,100,350,240
        // 2. 左上角 NW: (100,100,300,200) dx=30 dy=20 west=true north=true
        //    -> 130,120,270,180（right=400,bottom=300 不变）
        // 3. 右上角 NE: (100,100,300,200) dx=-50 dy=-30 west=false north=true
        //    -> 100,70,250,230（left/bottom 不变）
        // 4. 左下角 SW: (100,100,300,200) dx=-40 dy=25 west=true north=false
        //    -> 60,100,340,225（right=400 不变）
        // 5. 最小限制: (100,100,300,200) dx=1000 dy=1000 west=true north=true min=240x64
        //    -> 160,236,240,64（对边 right=400,bottom=300 不变）
        // 6. 宽高独立: (100,100,300,200) dx=0 dy=60 west=false north=false
        //    -> 100,100,300,260（宽不受 dy 影响，Left/Top 不动）
        // 7. 负坐标: (-200,-150,300,200) dx=-500 dy=-400 west=false north=false min=240x64
        //    -> -200,-150,240,64（左下边固定，允许负 Left/Top）
        // 8. NaN: (100,100,300,200) dx=NaN dy=NaN west=true north=true
        //    -> 100,100,300,200（NaN 增量按 0 处理，不产生 NaN 结果）
    }
}
