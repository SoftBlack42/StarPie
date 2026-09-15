using System;
using System.Windows;

namespace WinPieGestures;

public class RawMouseEventArgs : EventArgs
{
	public int Message { get; internal set; }

	public string MouseButton { get; internal set; }

	public uint MouseData { get; internal set; }

	public bool IsButtonDown { get; internal set; }

	public Point Position { get; set; }

	public bool Handled { get; set; }

	public RawMouseEventArgs(int message, string mouseButton, uint mouseData, bool isButtonDown, double x, double y)
	{
		Message = message;
		MouseButton = mouseButton;
		MouseData = mouseData;
		IsButtonDown = isButtonDown;
		Position = new Point(x, y);
		Handled = false;
	}

	/// <summary>复用实例：重置按键信息与 Handled 标记。仅限钩子回调线程在派发前调用，订阅方不得跨线程持有实例。</summary>
	internal void Update(int message, string mouseButton, uint mouseData, bool isButtonDown, double x, double y)
	{
		Message = message;
		MouseButton = mouseButton;
		MouseData = mouseData;
		IsButtonDown = isButtonDown;
		Position = new Point(x, y);
		Handled = false;
	}
}
