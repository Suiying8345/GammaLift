using System;
using System.Windows.Forms;

namespace GammaLift
{
    /// <summary>Gamma 曲线预览控件。
    /// 普通 PictureBox 在窗口缩放时由系统拷贝旧像素、只重绘新暴露的区域,
    /// 会残留上一个尺寸的曲线（同屏出现两条错位的对角线）。
    /// 这里开启双缓冲并让每次尺寸变化都整块失效重绘。</summary>
    internal sealed class CurveView : PictureBox
    {
        public CurveView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw, true);
        }
    }
}
