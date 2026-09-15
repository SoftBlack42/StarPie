using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinPieGestures;

public static class IconHelper
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct SHFILEINFO
	{
		public nint hIcon;

		public int iIcon;

		public uint dwAttributes;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string szDisplayName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
		public string szTypeName;
	}

	public class CustomIconItem
	{
		public string Key { get; set; } = "";

		public string DisplayName { get; set; } = "";

		public string FilePath { get; set; } = "";

		public string SvgData { get; set; } = "";

		public bool IsSvg => !string.IsNullOrEmpty(SvgData);
	}

	private const uint SHGFI_ICON = 256u;

	private const uint SHGFI_LARGEICON = 0u;

	private const uint SHGFI_USEFILEATTRIBUTES = 16u;

	private static readonly Guid IShellItemImageFactoryGuid;

	private static readonly ConcurrentDictionary<string, BitmapSource> _pinnedIcons;
	private static readonly ConcurrentDictionary<string, BitmapSource> _dynamicIcons;
	private static readonly LinkedList<string> _dynamicLruList;
	private static readonly object _dynamicLruLock;
	private const int MaxDynamicIcons = 120;

	public static readonly List<VectorIconItem> VectorIconList;

	private static readonly Dictionary<string, string> IconMap;

	private static List<CustomIconItem>? _cachedCustomIcons;

	[DllImport("shell32.dll", CharSet = CharSet.Auto)]
	private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

	[DllImport("shell32.dll", CharSet = CharSet.Auto)]
	private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out nint phiconLarge, out nint phiconSmall, uint nIcons);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SHCreateItemFromParsingName([In][MarshalAs(UnmanagedType.LPWStr)] string pszPath, [In] nint pbc, [In][MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool DestroyIcon(nint hIcon);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(nint hObject);

	static IconHelper()
	{
		IShellItemImageFactoryGuid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
		_pinnedIcons = new ConcurrentDictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
		_dynamicIcons = new ConcurrentDictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
		_dynamicLruList = new LinkedList<string>();
		_dynamicLruLock = new object();
		VectorIconList = new List<VectorIconItem>
		{
			new VectorIconItem
			{
				Key = "Command",
				Category = "快捷工具",
				DisplayName = "运行命令 (Command)",
				SvgData = "M4,3H20A2,2 0 0,1 22,5V19A2,2 0 0,1 20,21H4A2,2 0 0,1 2,19V5A2,2 0 0,1 4,3M4,5V19H20V5H4M11,17L7,13L11,9L12.4,10.4L9.8,13L12.4,15.6L11,17M15,17H18V15H15V17Z"
			},
			new VectorIconItem
			{
				Key = "Tile",
				Category = "窗口管理",
				DisplayName = "平铺窗口 (Tile)",
				SvgData = "M3,3H11V11H3Z M13,3H21V11H13Z M3,13H11V21H3Z M13,13H21V21H13Z"
			},
			new VectorIconItem
			{
				Key = "Copy",
				Category = "编辑与剪贴板",
				DisplayName = "复制 (Copy)",
				SvgData = "M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z"
			},
			new VectorIconItem
			{
				Key = "Paste",
				Category = "编辑与剪贴板",
				DisplayName = "粘贴 (Paste)",
				SvgData = "M19,20H5V4H7V7H17V4H19M12,2A1,1 0 0,1 13,3A1,1 0 0,1 12,4A1,1 0 0,1 11,3A1,1 0 0,1 12,2M19,2H14.82C14.4,0.84 13.3,0 12,0C10.7,0 9.6,0.84 9.18,2H5A2,2 0 0,0 3,4V20A2,2 0 0,0 5,22H19A2,2 0 0,0 21,20V4A2,2 0 0,0 19,2Z"
			},
			new VectorIconItem
			{
				Key = "Cut",
				Category = "编辑与剪贴板",
				DisplayName = "剪切 (Cut)",
				SvgData = "M9.64,7.64C9.87,7.14 10,6.59 10,6A4,4 0 0,0 6,2A4,4 0 0,0 2,6A4,4 0 0,0 6,10C6.59,10 7.14,9.87 7.64,9.64L10,12L7.64,14.36C7.14,14.13 6.59,14 6,14A4,4 0 0,0 2,18A4,4 0 0,0 6,22A4,4 0 0,0 10,18C10,17.41 9.87,16.86 9.64,16.36L12,14L19,21H22L13.5,12.5L16.36,9.64C16.86,9.87 17.41,10 18,10A4,4 0 0,0 22,6A4,4 0 0,0 18,2A4,4 0 0,0 14,6C14,6.59 14.13,7.14 14.36,7.64L12,10L9.64,7.64M6,4A2,2 0 0,1 8,6A2,2 0 0,1 6,8A2,2 0 0,1 4,6A2,2 0 0,1 6,4M6,16A2,2 0 0,1 8,18A2,2 0 0,1 6,20A2,2 0 0,1 4,18A2,2 0 0,1 6,16M18,4A2,2 0 0,1 20,6A2,2 0 0,1 18,8A2,2 0 0,1 16,6A2,2 0 0,1 18,4Z"
			},
			new VectorIconItem
			{
				Key = "Undo",
				Category = "编辑与剪贴板",
				DisplayName = "撤销 (Undo)",
				SvgData = "M12.5,8C9.85,8 7.45,8.97 5.6,10.6L2,7V16H11L7.38,12.38C8.77,11.22 10.54,10.5 12.5,10.5C16.04,10.5 19.05,12.81 20.1,16L22.47,15.22C21.08,11.03 17.15,8 12.5,8Z"
			},
			new VectorIconItem
			{
				Key = "Redo",
				Category = "编辑与剪贴板",
				DisplayName = "重做 (Redo)",
				SvgData = "M18.4,10.6C16.55,8.97 14.15,8 11.5,8C6.85,8 2.92,11.03 1.53,15.22L3.9,16C4.95,12.81 7.96,10.5 11.5,10.5C13.46,10.5 15.23,11.22 16.62,12.38L13,16H22V7L18.4,10.6Z"
			},
			new VectorIconItem
			{
				Key = "Save",
				Category = "编辑与剪贴板",
				DisplayName = "保存 (Save)",
				SvgData = "M15,9H5V5H15M12,19A3,3 0 0,1 9,16A3,3 0 0,1 12,13A3,3 0 0,1 15,16A3,3 0 0,1 12,19M17,3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V7L17,3Z"
			},
			new VectorIconItem
			{
				Key = "Search",
				Category = "编辑与剪贴板",
				DisplayName = "搜索查找 (Search)",
				SvgData = "M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z"
			},
			new VectorIconItem
			{
				Key = "CloseWindow",
				Category = "窗口管理",
				DisplayName = "关闭当前窗口 (Close)",
				SvgData = "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z"
			},
			new VectorIconItem
			{
				Key = "Minimize",
				Category = "窗口管理",
				DisplayName = "最小化窗口 (Minimize)",
				SvgData = "M20,14H4V10H20"
			},
			new VectorIconItem
			{
				Key = "Maximize",
				Category = "窗口管理",
				DisplayName = "最大化/还原 (Maximize)",
				SvgData = "M4,4H20V20H4V4M6,6V18H18V6H6Z"
			},
			new VectorIconItem
			{
				Key = "SnapLeft",
				Category = "窗口管理",
				DisplayName = "左半屏贴靠 (Snap Left)",
				SvgData = "M4,4H20A2,2 0 0,1 22,6V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4M4,6V18H11V6H4M13,6V18H20V6H13Z"
			},
			new VectorIconItem
			{
				Key = "SnapRight",
				Category = "窗口管理",
				DisplayName = "右半屏贴靠 (Snap Right)",
				SvgData = "M4,4H20A2,2 0 0,1 22,6V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4M4,6V18H11V6H4M13,6V18H20V6H13Z"
			},
			new VectorIconItem
			{
				Key = "TaskView",
				Category = "窗口管理",
				DisplayName = "任务视图/多任务 (Task View)",
				SvgData = "M4,4H10V10H4V4M14,4H20V10H14V4M4,14H10V20H4V14M14,14H20V20H14V14Z"
			},
			new VectorIconItem
			{
				Key = "PrevDesktop",
				Category = "窗口管理",
				DisplayName = "上一虚拟桌面 (Prev Desktop)",
				SvgData = "M4,2H20A2,2 0 0,1 22,4V16A2,2 0 0,1 20,18H14V20H16V22H8V20H10V18H4A2,2 0 0,1 2,16V4A2,2 0 0,1 4,2M13,6L8,10L13,14V11H17V9H13V6Z"
			},
			new VectorIconItem
			{
				Key = "NextDesktop",
				Category = "窗口管理",
				DisplayName = "下一虚拟桌面 (Next Desktop)",
				SvgData = "M4,2H20A2,2 0 0,1 22,4V16A2,2 0 0,1 20,18H14V20H16V22H8V20H10V18H4A2,2 0 0,1 2,16V4A2,2 0 0,1 4,2M11,6V9H7V11H11V14L16,10L11,6Z"
			},
			new VectorIconItem
			{
				Key = "ShowDesktop",
				Category = "窗口管理",
				DisplayName = "显示桌面 (Desktop)",
				SvgData = "M4,2A2,2,0,0,0,2,4V16A2,2,0,0,0,4,18H10V20H8V22H16V20H14V18H20A2,2,0,0,0,22,16V4A2,2,0,0,0,20,2H4ZM4,4H20V16H4V4Z"
			},
			new VectorIconItem
			{
				Key = "FullScreen",
				Category = "窗口管理",
				DisplayName = "全屏切换 (Full Screen)",
				SvgData = "M5,5H10V7H7V10H5V5M14,5H19V10H17V7H14V5M17,14H19V19H14V17H17V14M10,17V19H5V14H7V17H10Z"
			},
			new VectorIconItem
			{
				Key = "Screenshot",
				Category = "窗口管理",
				DisplayName = "屏幕截图 (Screenshot)",
				SvgData = "M4,4H7L9,2H15L17,4H20A2,2,0,0,1,22,6V18A2,2,0,0,1,20,20H4A2,2,0,0,1,2,18V6A2,2,0,0,1,4,4ZM12,7A5,5,0,1,0,17,12A5,5,0,0,0,12,7ZM12,9A3,3,0,1,1,9,12A3,3,0,0,1,12,9Z"
			},
			new VectorIconItem
			{
				Key = "Back",
				Category = "网页浏览",
				DisplayName = "后退 (Back)",
				SvgData = "M20,11H7.83L13.42,5.41L12,4L4,12L12,20L13.41,18.59L7.83,13H20V11Z"
			},
			new VectorIconItem
			{
				Key = "Forward",
				Category = "网页浏览",
				DisplayName = "前进 (Forward)",
				SvgData = "M12,4L10.59,5.41L16.17,11H4V13H16.17L10.59,18.59L12,20L20,12L12,4Z"
			},
			new VectorIconItem
			{
				Key = "Refresh",
				Category = "网页浏览",
				DisplayName = "刷新 (Refresh)",
				SvgData = "M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z"
			},
			new VectorIconItem
			{
				Key = "NewTab",
				Category = "网页浏览",
				DisplayName = "新建标签页 (New Tab)",
				SvgData = "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z"
			},
			new VectorIconItem
			{
				Key = "CloseTab",
				Category = "网页浏览",
				DisplayName = "关闭标签页 (Close Tab)",
				SvgData = "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z"
			},
			new VectorIconItem
			{
				Key = "ReopenTab",
				Category = "网页浏览",
				DisplayName = "恢复标签页 (Reopen Tab)",
				SvgData = "M13,3A9,9 0 0,0 4,12H1L4.89,15.89L4.96,16.03L9,12H6A7,7 0 0,1 13,5A7,7 0 0,1 20,12A7,7 0 0,1 13,19C11.07,19 9.32,18.21 8.06,16.94L6.64,18.36C8.27,20 10.5,21 13,21A9,9 0 0,0 22,12A9,9 0 0,0 13,3Z"
			},
			new VectorIconItem
			{
				Key = "ZoomIn",
				Category = "网页浏览",
				DisplayName = "页面放大 (Zoom In)",
				SvgData = "M15.5,14H14.71L14.44,13.73C15.41,12.59 16,11.11 16,9.5A6.5,6.5 0 1,0 9.5,16C11.11,16 12.59,15.41 13.73,14.44L14.71,14H15.5L20.5,19L19,20.5L14,15.5M9.5,14C7,14 5,12 5,9.5C5,7 7,5 9.5,5C12,5 14,7 14,9.5C14,12 12,14 9.5,14M12,10H10V12H9V10H7V9H9V7H10V9H12V10Z"
			},
			new VectorIconItem
			{
				Key = "ZoomOut",
				Category = "网页浏览",
				DisplayName = "页面缩小 (Zoom Out)",
				SvgData = "M15.5,14H14.71L14.44,13.73C15.41,12.59 16,11.11 16,9.5A6.5,6.5 0 1,0 9.5,16C11.11,16 12.59,15.41 13.73,14.44L14.71,14H15.5L20.5,19L19,20.5L14,15.5M9.5,14C7,14 5,12 5,9.5C5,7 7,5 9.5,5C12,5 14,7 14,9.5C14,12 12,14 9.5,14M7,9H12V10H7V9Z"
			},
			new VectorIconItem
			{
				Key = "VolumeUp",
				Category = "多媒体与系统",
				DisplayName = "音量增加 (Volume Up)",
				SvgData = "M3,9V15H7L12,20V4L7,9H3ZM14,3.23V5.29C16.89,6.15 19,8.83 19,12C19,15.17 16.89,17.85 14,18.71V20.77C18.01,19.86 21,16.28 21,12C21,7.72 18.01,4.14 14,3.23ZM14,8.83V15.17C15.14,14.6 16,13.4 16,12C16,10.6 15.14,9.4 14,8.83Z"
			},
			new VectorIconItem
			{
				Key = "VolumeDown",
				Category = "多媒体与系统",
				DisplayName = "音量减小 (Volume Down)",
				SvgData = "M3,9V15H7L12,20V4L7,9H3ZM14,8.83V15.17C15.14,14.6 16,13.4 16,12C16,10.6 15.14,9.4 14,8.83ZM14,3.23V5.29C16.89,6.15 19,8.83 19,12C19,15.17 16.89,17.85 14,18.71V20.77C18.01,19.86 21,16.28 21,12Z"
			},
			new VectorIconItem
			{
				Key = "VolumeMute",
				Category = "多媒体与系统",
				DisplayName = "静音切换 (Mute)",
				SvgData = "M3,9V15H7L12,20V4L7,9H3ZM16.5,12L14,9.5L15.5,8L18,10.5L20.5,8L22,9.5L19.5,12L22,14.5L20.5,16L18,13.5L15.5,16L14,14.5L16.5,12Z"
			},
			new VectorIconItem
			{
				Key = "PlayPause",
				Category = "多媒体与系统",
				DisplayName = "播放/暂停 (Play/Pause)",
				SvgData = "M3,5V19L11.5,12Z M13.5,5H16.5V19H13.5Z M18.5,5H21.5V19H18.5Z"
			},
			new VectorIconItem
			{
				Key = "NextTrack",
				Category = "多媒体与系统",
				DisplayName = "下一曲 (Next)",
				SvgData = "M16,18H18V6H16M6,18L14.5,12L6,6V18Z"
			},
			new VectorIconItem
			{
				Key = "PrevTrack",
				Category = "多媒体与系统",
				DisplayName = "上一曲 (Prev)",
				SvgData = "M6,6H8V18H6M9.5,12L18,18V6L9.5,12Z"
			},
			new VectorIconItem
			{
				Key = "Lock",
				Category = "多媒体与系统",
				DisplayName = "锁定电脑 (Lock)",
				SvgData = "M18,8H17V6A5,5,0,0,0,7,6V8H6A2,2,0,0,0,4,10V20A2,2,0,0,0,6,22H18A2,2,0,0,0,20,20V10A2,2,0,0,0,18,8ZM9,6A3,3,0,0,1,15,6V8H9ZM18,20H6V10H18Z"
			},
			new VectorIconItem
			{
				Key = "Settings",
				Category = "多媒体与系统",
				DisplayName = "控制面板/设置 (Settings)",
				SvgData = "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z"
			},
			new VectorIconItem
			{
				Key = "Sleep",
				Category = "多媒体与系统",
				DisplayName = "系统睡眠 (Sleep)",
				SvgData = "M12.3,2A10,10 0 0,0 1.9,14.42C2.5,14.78 3.18,15 3.9,15A8.1,8.1 0 0,0 12,6.9C12,6.18 11.78,5.5 11.42,4.9A10,10 0 0,0 12.3,2Z"
			},
			new VectorIconItem
			{
				Key = "Restart",
				Category = "多媒体与系统",
				DisplayName = "重启电脑 (Restart)",
				SvgData = "M12,4V1L8,5L12,9V6A6,6 0 0,1 18,12A6,6 0 0,1 12,18A6,6 0 0,1 6.34,14H4.26A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4Z"
			},
			new VectorIconItem
			{
				Key = "Shutdown",
				Category = "多媒体与系统",
				DisplayName = "关闭电脑 (Shutdown)",
				SvgData = "M16.56,5.44L15.11,6.89C16.84,8.14 18,10.16 18,12.5A6,6 0 0,1 12,18.5A6,6 0 0,1 6,12.5C6,10.16 7.16,8.14 8.89,6.89L7.44,5.44C5.36,6.99 4,9.59 4,12.5A8,8 0 0,0 12,20.5A8,8 0 0,0 20,12.5C20,9.59 18.64,6.99 16.56,5.44M13,3H11V13H13V3Z"
			},
			new VectorIconItem
			{
				Key = "TaskManager",
				Category = "生产力工具",
				DisplayName = "任务管理器 (Task Manager)",
				SvgData = "M19,3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3M19,19H5V5H19V19M7,10H9V17H7V10M11,7H13V17H11V7M15,13H17V17H15V13Z"
			},
			new VectorIconItem
			{
				Key = "Explorer",
				Category = "生产力工具",
				DisplayName = "文件资源管理器 (Explorer)",
				SvgData = "M19,20H5A2,2 0 0,1 3,18V6A2,2 0 0,1 5,4H10L12,6H19A2,2 0 0,1 21,8V18A2,2 0 0,1 19,20M5,8V18H19V8H5Z"
			},
			new VectorIconItem
			{
				Key = "Folder",
				Category = "生产力工具",
				DisplayName = "打开文件夹 (Folder)",
				SvgData = "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z"
			},
			new VectorIconItem
			{
				Key = "ClipboardHistory",
				Category = "生产力工具",
				DisplayName = "剪贴板历史 (Clipboard History)",
				SvgData = "M19,3H14.82C14.4,1.84 13.3,1 12,1C10.7,1 9.6,1.84 9.18,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5A2,2 0 0,0 19,3M12,3A1,1 0 0,1 13,4A1,1 0 0,1 12,5A1,1 0 0,1 11,4A1,1 0 0,1 12,3M7,7H17V9H7V7M7,11H17V13H7V11M7,15H14V17H7V15Z"
			},
			new VectorIconItem
			{
				Key = "RunDialog",
				Category = "生产力工具",
				DisplayName = "运行窗口 (Run Dialog)",
				SvgData = "M20,4H4A2,2 0 0,0 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V6A2,2 0 0,0 20,4M20,18H4V8H20V18M6,14L10,11L6,8V14M11,15H17V13H11V15Z"
			},
			new VectorIconItem
			{
				Key = "Terminal",
				Category = "生产力工具",
				DisplayName = "命令行终端 (Terminal)",
				SvgData = "M20,4H4A2,2 0 0,0 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V6A2,2 0 0,0 20,4M20,18H4V8H20V18M6,10L10,13L6,16V10M11,16H17V14H11V16Z"
			},
			new VectorIconItem
			{
				Key = "Code",
				Category = "生产力工具",
				DisplayName = "代码编程 (Code)",
				SvgData = "M14.6,16.6L19.2,12L14.6,7.4L16,6L22,12L16,18L14.6,16.6M9.4,16.6L4.8,12L9.4,7.4L8,6L2,12L8,18L9.4,16.6Z"
			},
			new VectorIconItem
			{
				Key = "Calculator",
				Category = "生产力工具",
				DisplayName = "计算器 (Calculator)",
				SvgData = "M19,3H5C3.9,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.9 20.1,3 19,3M19,7H5V5H19V7M7,9H9V11H7V9M11,9H13V11H11V9M15,9H17V11H15V9M7,13H9V15H7V13M11,13H13V15H11V13M15,13H17V15H15V13M7,17H9V19H7V17M11,17H13V19H11V17M15,17H17V19H15V17Z"
			}
		};
		IconMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (VectorIconItem vectorIcon in VectorIconList)
		{
			IconMap[vectorIcon.Key] = vectorIcon.SvgData;
		}
	}

	public static string? GetSvgPathByKey(string? key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return null;
		}
		if (IconMap.TryGetValue(key, out string value))
		{
			return value;
		}
		// 插件贡献的矢量图标：key 形如 plugin:<pluginId>:<shortKey>（见 PluginApi.IconKeyPrefix）。
		// 放在内置图标之后、裸 path 判定之前 —— 前缀唯一，不会与既有 key 撞车；
		// 而放在裸 path 判定之前是因为插件 key 一定不是以 "M" 开头的路径数据。
		if (key.StartsWith(StarPie.Plugin.PluginApi.IconKeyPrefix, StringComparison.OrdinalIgnoreCase))
		{
			string? pluginSvg = Plugins.PluginHost.Catalog.ResolveIcon(key);
			if (!string.IsNullOrEmpty(pluginSvg))
			{
				return pluginSvg;
			}
		}
		if (key.Trim().StartsWith("M", StringComparison.OrdinalIgnoreCase) && key.Contains(","))
		{
			return key.Trim();
		}
		CustomIconItem customIconItem = GetCustomIcons().FirstOrDefault((CustomIconItem c) => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase) || string.Equals(c.DisplayName, key, StringComparison.OrdinalIgnoreCase));
		if (customIconItem != null)
		{
			if (!string.IsNullOrEmpty(customIconItem.SvgData))
			{
				return customIconItem.SvgData;
			}
			if (customIconItem.IsSvg && File.Exists(customIconItem.FilePath))
			{
				try
				{
					string text = ExtractSvgPathData(File.ReadAllText(customIconItem.FilePath));
					if (!string.IsNullOrEmpty(text))
					{
						customIconItem.SvgData = text;
						return text;
					}
				}
				catch
				{
				}
			}
		}
		return null;
	}

	public static Geometry CreateAdvancedSectorGeometry(double cx, double cy, double startAngle, double endAngle, double innerR, double outerR, string shape, double gap = 0.0, double cornerRadius = 0.0)
	{
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_024f: Unknown result type (might be due to invalid IL or missing references)
		double num = (startAngle + endAngle) / 2.0;
		double num2 = num * (Math.PI / 180.0);
		double num3 = (innerR + outerR) / 2.0;
		double num4 = cx + Math.Cos(num2) * num3;
		double num5 = cy + Math.Sin(num2) * num3;
		double num6 = Math.Abs(endAngle - startAngle);
		double a = num6 / 2.0 * (Math.PI / 180.0);
		double num7 = 2.0 * num3 * Math.Sin(a);
		double num8 = Math.Max(12.0, outerR - innerR - gap);
		double num9 = Math.Max(12.0, num7 - gap);
		switch (shape)
		{
		case "Circle":
		{
			double num16 = Math.Max(12.0, Math.Min(num8, num9) * 0.94);
			return new EllipseGeometry(new Point(num4, num5), num16 / 2.0, num16 / 2.0);
		}
		case "HexagonHive":
		{
			double radius = Math.Max(10.0, Math.Min(num8 / 2.0, num9 / Math.Sqrt(3.0)) * 0.95);
			return CreateHexagonGeometry(num4, num5, radius, cornerRadius, num);
		}
		case "RoundedCapsule":
		case "FloatingCapsules":
		case "RoundedRect":
		case "Capsule":
		{
			double num13 = Math.Max(16.0, num8 * 0.96);
			double num14 = Math.Max(16.0, Math.Min(num13 * 0.82, num9 * 0.88));
			double num15 = ((cornerRadius > 0.0) ? Math.Min(num14 / 2.0, cornerRadius + 2.0) : Math.Min(num14 / 2.0, 10.0));
			RectangleGeometry rect = new RectangleGeometry(new Rect(num4 - num13 / 2.0, num5 - num14 / 2.0, num13, num14), num15, num15)
			{
				Transform = new RotateTransform(num, num4, num5)
			};
			rect.Freeze();
			return rect;
		}
		default:
		{
			double num10 = startAngle;
			double num11 = endAngle;
			if (gap > 0.0 && num3 > 0.0)
			{
				double num12 = gap / num3 * (180.0 / Math.PI);
				if (num12 < num6 * 0.6)
				{
					num10 += num12 / 2.0;
					num11 -= num12 / 2.0;
				}
			}
			return CreateRoundedAnnularSectorGeometry(cx, cy, num10, num11, innerR, outerR, cornerRadius);
		}
		}
	}

	public static Geometry CreateHexagonGeometry(double cx, double cy, double radius, double cornerRadius = 0.0, double rotationDegrees = 0.0)
	{
		double maxFillet = Math.Sqrt(3.0) / 2.0 * radius * 0.95;
		double effectiveCr = Math.Max(0.0, Math.Min(cornerRadius, maxFillet));
		double rotRad = rotationDegrees * (Math.PI / 180.0);

		Point[] vertices = new Point[6];
		for (int i = 0; i < 6; i++)
		{
			double ang = rotRad + (double)i * (Math.PI / 3.0);
			vertices[i] = new Point(cx + radius * Math.Cos(ang), cy + radius * Math.Sin(ang));
		}

		StreamGeometry hex = new StreamGeometry();
		using (StreamGeometryContext ctx = hex.Open())
		{
			if (effectiveCr < 0.5)
			{
				ctx.BeginFigure(vertices[0], isFilled: true, isClosed: true);
				for (int i = 1; i < 6; i++)
				{
					ctx.LineTo(vertices[i], isStroked: true, isSmoothJoin: false);
				}
			}
			else
			{
				double tangentDist = effectiveCr / Math.Sqrt(3.0);
				Point[] pEntry = new Point[6];
				Point[] pExit = new Point[6];

				for (int k = 0; k < 6; k++)
				{
					Point prev = vertices[(k + 5) % 6];
					Point curr = vertices[k];
					Point next = vertices[(k + 1) % 6];

					Vector vIn = curr - prev;
					vIn.Normalize();
					pEntry[k] = curr - vIn * tangentDist;

					Vector vOut = next - curr;
					vOut.Normalize();
					pExit[k] = curr + vOut * tangentDist;
				}

				ctx.BeginFigure(pEntry[0], isFilled: true, isClosed: true);
				Size arcSize = new Size(effectiveCr, effectiveCr);

				for (int i = 0; i < 6; i++)
				{
					ctx.ArcTo(pExit[i], arcSize, 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
					int nextIdx = (i + 1) % 6;
					ctx.LineTo(pEntry[nextIdx], isStroked: true, isSmoothJoin: false);
				}
			}
		}
		hex.Freeze();
		return hex;
	}

	public static Geometry CreateRoundedAnnularSectorGeometry(double cx, double cy, double startAngle, double endAngle, double innerRadius, double outerRadius, double cornerRadius)
	{
		//IL_0282: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0300: Unknown result type (might be due to invalid IL or missing references)
		//IL_031f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0325: Unknown result type (might be due to invalid IL or missing references)
		//IL_0348: Unknown result type (might be due to invalid IL or missing references)
		//IL_034a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0369: Unknown result type (might be due to invalid IL or missing references)
		//IL_037d: Unknown result type (might be due to invalid IL or missing references)
		//IL_037f: Unknown result type (might be due to invalid IL or missing references)
		double num = endAngle - startAngle;
		if (num <= 0.0)
		{
			return Geometry.Empty;
		}
		double num2 = outerRadius - innerRadius;
		double num3 = 2.0 * innerRadius * Math.Sin(num / 2.0 * (Math.PI / 180.0));
		double val = Math.Max(0.0, Math.Min(num2 / 2.2, num3 / 2.2));
		double num4 = Math.Max(0.0, Math.Min(cornerRadius, val));
		if (num4 < 0.8)
		{
			return CreateStandardSectorGeometry(cx, cy, startAngle, endAngle, innerRadius, outerRadius);
		}
		try
		{
			double num5 = startAngle * (Math.PI / 180.0);
			double num6 = endAngle * (Math.PI / 180.0);
			double num7 = Math.Asin(Math.Min(0.95, num4 / Math.Max(1.0, outerRadius - num4)));
			double num8 = Math.Asin(Math.Min(0.95, num4 / Math.Max(1.0, innerRadius + num4)));
			double num9 = num5 + num7;
			double num10 = num6 - num7;
			double num11 = num5 + num8;
			double num12 = num6 - num8;
			if (num10 <= num9 || num12 <= num11)
			{
				return CreateStandardSectorGeometry(cx, cy, startAngle, endAngle, innerRadius, outerRadius);
			}
			Point val2 = default(Point);
			val2 = new Point(cx + Math.Cos(num9) * outerRadius, cy + Math.Sin(num9) * outerRadius);
			Point point = default(Point);
			point = new Point(cx + Math.Cos(num10) * outerRadius, cy + Math.Sin(num10) * outerRadius);
			Point point2 = default(Point);
			point2 = new Point(cx + Math.Cos(num6) * (outerRadius - num4), cy + Math.Sin(num6) * (outerRadius - num4));
			Point point3 = default(Point);
			point3 = new Point(cx + Math.Cos(num6) * (innerRadius + num4), cy + Math.Sin(num6) * (innerRadius + num4));
			Point point4 = default(Point);
			point4 = new Point(cx + Math.Cos(num12) * innerRadius, cy + Math.Sin(num12) * innerRadius);
			Point point5 = default(Point);
			point5 = new Point(cx + Math.Cos(num11) * innerRadius, cy + Math.Sin(num11) * innerRadius);
			Point point6 = default(Point);
			point6 = new Point(cx + Math.Cos(num5) * (innerRadius + num4), cy + Math.Sin(num5) * (innerRadius + num4));
			Point point7 = default(Point);
			point7 = new Point(cx + Math.Cos(num5) * (outerRadius - num4), cy + Math.Sin(num5) * (outerRadius - num4));
			bool isLargeArc = Math.Abs(num) > 180.0;
			Size size = default(Size);
			size = new Size(num4, num4);
			PathFigure pathFigure = new PathFigure
			{
				StartPoint = val2,
				IsClosed = true,
				IsFilled = true
			};
			pathFigure.Segments.Add(new ArcSegment(point, new Size(outerRadius, outerRadius), 0.0, isLargeArc, SweepDirection.Clockwise, isStroked: true));
			pathFigure.Segments.Add(new ArcSegment(point2, size, 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true));
			pathFigure.Segments.Add(new LineSegment(point3, isStroked: true));
			pathFigure.Segments.Add(new ArcSegment(point4, size, 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true));
			pathFigure.Segments.Add(new ArcSegment(point5, new Size(innerRadius, innerRadius), 0.0, isLargeArc, SweepDirection.Counterclockwise, isStroked: true));
			pathFigure.Segments.Add(new ArcSegment(point6, size, 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true));
			pathFigure.Segments.Add(new LineSegment(point7, isStroked: true));
			pathFigure.Segments.Add(new ArcSegment(val2, size, 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true));
			return new PathGeometry
			{
				Figures = { pathFigure }
			};
		}
		catch
		{
			return CreateStandardSectorGeometry(cx, cy, startAngle, endAngle, innerRadius, outerRadius);
		}
	}

	private static Geometry CreateStandardSectorGeometry(double cx, double cy, double startAngle, double endAngle, double innerRadius, double outerRadius)
	{
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		double num = startAngle * (Math.PI / 180.0);
		double num2 = endAngle * (Math.PI / 180.0);
		Point startPoint = default(Point);
		startPoint = new Point(cx + Math.Cos(num) * outerRadius, cy + Math.Sin(num) * outerRadius);
		Point point = default(Point);
		point = new Point(cx + Math.Cos(num2) * outerRadius, cy + Math.Sin(num2) * outerRadius);
		Point point2 = default(Point);
		point2 = new Point(cx + Math.Cos(num2) * innerRadius, cy + Math.Sin(num2) * innerRadius);
		Point point3 = default(Point);
		point3 = new Point(cx + Math.Cos(num) * innerRadius, cy + Math.Sin(num) * innerRadius);
		bool isLargeArc = Math.Abs(endAngle - startAngle) > 180.0;
		PathFigure pathFigure = new PathFigure
		{
			StartPoint = startPoint,
			IsClosed = true,
			IsFilled = true
		};
		pathFigure.Segments.Add(new ArcSegment(point, new Size(Math.Max(1.0, outerRadius), Math.Max(1.0, outerRadius)), 0.0, isLargeArc, SweepDirection.Clockwise, isStroked: true));
		pathFigure.Segments.Add(new LineSegment(point2, isStroked: true));
		pathFigure.Segments.Add(new ArcSegment(point3, new Size(Math.Max(1.0, innerRadius), Math.Max(1.0, innerRadius)), 0.0, isLargeArc, SweepDirection.Counterclockwise, isStroked: true));
		return new PathGeometry
		{
			Figures = { pathFigure }
		};
	}

	public static Geometry GetCoreIconGeometry(string? coreIconType, string? customKey = null, string? customSvg = null)
	{
		switch (string.IsNullOrEmpty(coreIconType) ? "Exit" : coreIconType)
		{
		case "Crosshair":
			return Geometry.Parse("M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4M11,6V11H6V13H11V18H13V13H18V11H13V6H11Z");
		case "Windows":
			return Geometry.Parse("M3,12V6.75L9,5.92V12M20,3V12H10V5.78L20,3M3,13H9V18.08L3,17.25M10,13H20V21L10,18.22");
		case "Dot":
		case "Bullseye":
			return Geometry.Parse("M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7Z");
		case "Home":
			return Geometry.Parse("M10,20V14H14V20H19V12H22L12,3L2,12H5V20H10Z");
		case "Power":
			return Geometry.Parse("M16.56,5.44L15.11,6.89C16.84,8.14 18,10.16 18,12.5A6,6 0 0,1 12,18.5A6,6 0 0,1 6,12.5C6,10.16 7.16,8.14 8.89,6.89L7.44,5.44C5.36,6.99 4,9.59 4,12.5A8,8 0 0,0 12,20.5A8,8 0 0,0 20,12.5C20,9.59 18.64,6.99 16.56,5.44M13,3H11V13H13V3Z");
		case "Compass":
			return Geometry.Parse("M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4M14.19,14.19L6,18L9.81,9.81L18,6L14.19,14.19M12,10.9A1.1,1.1 0 0,0 10.9,12A1.1,1.1 0 0,0 12,13.1A1.1,1.1 0 0,0 13.1,12A1.1,1.1 0 0,0 12,10.9Z");
		case "CatPaw":
			return Geometry.Parse("M12,14.5C10.5,14.5 9,15.5 8.5,17C8,18.5 9,20 10.5,20.5C11.5,20.8 12.5,20.8 13.5,20.5C15,20 16,18.5 15.5,17C15,15.5 13.5,14.5 12,14.5M6,12.5A2,2 0 0,0 4,14.5A2,2 0 0,0 6,16.5A2,2 0 0,0 8,14.5A2,2 0 0,0 6,12.5M18,12.5A2,2 0 0,0 16,14.5A2,2 0 0,0 18,16.5A2,2 0 0,0 20,14.5A2,2 0 0,0 18,12.5M9.5,8.5A2,2 0 0,0 7.5,10.5A2,2 0 0,0 9.5,12.5A2,2 0 0,0 11.5,10.5A2,2 0 0,0 9.5,8.5M14.5,8.5A2,2 0 0,0 12.5,10.5A2,2 0 0,0 14.5,12.5A2,2 0 0,0 16.5,10.5A2,2 0 0,0 14.5,8.5Z");
		case "Custom":
			if (!string.IsNullOrEmpty(customSvg))
			{
				try
				{
					return Geometry.Parse(customSvg);
				}
				catch
				{
				}
			}
			if (!string.IsNullOrEmpty(customKey))
			{
				string svgPathByKey = GetSvgPathByKey(customKey);
				if (!string.IsNullOrEmpty(svgPathByKey))
				{
					try
					{
						return Geometry.Parse(svgPathByKey);
					}
					catch
					{
					}
				}
			}
			return Geometry.Parse("M12,2L15.09,8.26L22,9.27L17,14.14L18.18,21.02L12,17.77L5.82,21.02L7,14.14L2,9.27L8.91,8.26L12,2Z");
		default:
			return Geometry.Parse("M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z");
		}
	}

	/// <summary>
	/// 判断当前全局配置中是否启用了自定义中心图案（包括本地图片贴图、自定义SVG/图标或除Exit外的预设图案）
	/// </summary>
	public static bool HasCustomCenterPattern(AppConfig? config)
	{
		if (config == null || !config.ShowCoreIcon)
		{
			return false;
		}

		string iconType = config.CoreIconType ?? "Exit";

		// 1. 本地自定义图片/贴图
		if (string.Equals(iconType, "Image", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(config.CoreCustomImagePath))
		{
			if (!string.IsNullOrEmpty(config.CoreCustomImagePath) && File.Exists(config.CoreCustomImagePath))
			{
				return true;
			}
			if (string.Equals(iconType, "Image", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(config.CoreCustomImagePath))
			{
				return true;
			}
		}

		// 2. 自定义矢量 SVG / 图标库
		if (string.Equals(iconType, "Custom", StringComparison.OrdinalIgnoreCase))
		{
			if (!string.IsNullOrEmpty(config.CoreCustomIconKey) || !string.IsNullOrEmpty(config.CoreCustomIconSvg))
			{
				return true;
			}
		}

		// 3. 预设图案类型（除默认取消退出 Exit 之外的任意图案，如准星、Windows、罗盘、猫爪等）
		if (!string.Equals(iconType, "Exit", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return false;
	}

	public static bool ResolveShortcutTarget(string lnkPath, out string targetPath, out string iconPath, out int iconIndex)
	{
		targetPath = "";
		iconPath = "";
		iconIndex = 0;
		if (string.IsNullOrEmpty(lnkPath) || !File.Exists(lnkPath))
		{
			return false;
		}
		try
		{
			ShellLink shellLink = new ShellLink();
			((IPersistFile)shellLink).Load(lnkPath, 0u);
			IShellLinkW obj = (IShellLinkW)shellLink;
			StringBuilder stringBuilder = new StringBuilder(260);
			obj.GetIconLocation(stringBuilder, stringBuilder.Capacity, out iconIndex);
			string text = Environment.ExpandEnvironmentVariables(stringBuilder.ToString().Trim());
			if (!string.IsNullOrEmpty(text) && (File.Exists(text) || Directory.Exists(text)))
			{
				iconPath = text;
			}
			StringBuilder stringBuilder2 = new StringBuilder(260);
			obj.GetPath(stringBuilder2, stringBuilder2.Capacity, IntPtr.Zero, 0u);
			string text2 = Environment.ExpandEnvironmentVariables(stringBuilder2.ToString().Trim());
			if (!string.IsNullOrEmpty(text2))
			{
				targetPath = text2;
			}
			return !string.IsNullOrEmpty(targetPath) || !string.IsNullOrEmpty(iconPath);
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static void CacheDynamicIcon(string key, BitmapSource icon)
	{
		lock (_dynamicLruLock)
		{
			if (_dynamicIcons.Count >= MaxDynamicIcons)
			{
				var oldest = _dynamicLruList.First;
				if (oldest != null)
				{
					_dynamicLruList.RemoveFirst();
					_dynamicIcons.TryRemove(oldest.Value, out _);
				}
			}
			_dynamicIcons[key] = icon;
			_dynamicLruList.Remove(key);
			_dynamicLruList.AddLast(key);
		}
	}

	/// <summary>
	/// 清理动态弹性图标缓存（文件搜索、程序挑选器等临时图标），归还非托管图像资源
	/// </summary>
	public static void TrimDynamicCache()
	{
		lock (_dynamicLruLock)
		{
			_dynamicIcons.Clear();
			_dynamicLruList.Clear();
		}
	}

	/// <summary>
	/// 将当前配置文件中所有轮盘方案所需的高频图标预载并永久锁定在第一层缓存中，
	/// 确保用户唤出轮盘时命中率 100%，耗时 0ms，绝无掉帧或硬缺页延迟。
	/// </summary>
	public static void PinIconsForConfig(AppConfig? config)
	{
		HashSet<string> requiredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (config?.Profiles != null)
		{
			foreach (var profile in config.Profiles)
			{
				if (profile?.Actions == null) continue;
				CollectPinnedIconPaths(profile.Actions, requiredPaths);
				if (profile.Layers != null)
				{
					foreach (var layer in profile.Layers)
					{
						if (layer?.Actions != null)
						{
							CollectPinnedIconPaths(layer.Actions, requiredPaths);
						}
					}
				}
			}
		}

		// 与当前配置做差量同步：保留仍在使用的图标，解除废弃图标的静态强引用。
		foreach (string cachedPath in _pinnedIcons.Keys)
		{
			if (!requiredPaths.Contains(cachedPath))
			{
				_pinnedIcons.TryRemove(cachedPath, out _);
			}
		}

		foreach (string requiredPath in requiredPaths)
		{
			PinIcon(requiredPath);
		}
	}

	private static void CollectPinnedIconPaths(IEnumerable<ActionItem> actions, ISet<string> paths)
	{
		foreach (var action in actions)
		{
			if (action == null) continue;
			AddPinnedIconPath(paths, action.InheritAppIconPath);
			if (string.Equals(action.Type, "Launch", StringComparison.OrdinalIgnoreCase))
			{
				AddPinnedIconPath(paths, action.Parameter);
			}
			if (action.SubActions != null && action.SubActions.Count > 0)
			{
				CollectPinnedIconPaths(action.SubActions, paths);
			}
		}
	}

	private static void AddPinnedIconPath(ISet<string> paths, string? path)
	{
		if (string.IsNullOrWhiteSpace(path)) return;
		string cleanPath = path.Trim().Trim('"');
		if (cleanPath.Length > 0)
		{
			paths.Add(cleanPath);
		}
	}

	public static void PinIcon(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return;
		string cleanPath = path.Trim().Trim('"');
		if (_pinnedIcons.ContainsKey(cleanPath)) return;

		BitmapSource? icon = ExtractIconRaw(cleanPath);
		if (icon != null)
		{
			_pinnedIcons[cleanPath] = icon;
		}
	}

	public static BitmapSource? GetIcon(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		string text = path.Trim().Trim('"');

		// 1. 优先从第一层常驻锁定缓存中取 (0ms)
		if (_pinnedIcons.TryGetValue(text, out BitmapSource? pinnedVal) && pinnedVal != null)
		{
			return pinnedVal;
		}

		// 2. 从第二层 LRU 动态弹性缓存中取
		if (_dynamicIcons.TryGetValue(text, out BitmapSource? dynamicVal) && dynamicVal != null)
		{
			lock (_dynamicLruLock)
			{
				_dynamicLruList.Remove(text);
				_dynamicLruList.AddLast(text);
			}
			return dynamicVal;
		}

		// 3. 提取图标并加入动态弹性缓存
		BitmapSource? extracted = ExtractIconRaw(text);
		if (extracted != null)
		{
			CacheDynamicIcon(text, extracted);
		}
		return extracted;
	}

	private static BitmapSource? ExtractIconRaw(string text)
	{
		try
		{
			string text2 = Environment.ExpandEnvironmentVariables(text);
			if (text2.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && ResolveShortcutTarget(text2, out string targetPath, out string iconPath, out int iconIndex))
			{
				if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
				{
					BitmapSource bitmapSource = ExtractPureIconFromFile(iconPath, iconIndex);
					if (bitmapSource != null)
					{
						return bitmapSource;
					}
				}
				if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
				{
					BitmapSource bitmapSource2 = ExtractPureIconFromFile(targetPath, 0);
					if (bitmapSource2 != null)
					{
						return bitmapSource2;
					}
				}
			}
			if (File.Exists(text2) || Directory.Exists(text2))
			{
				BitmapSource bitmapSource3 = ExtractPureIconFromFile(text2, 0);
				if (bitmapSource3 != null)
				{
					return bitmapSource3;
				}
			}
			BitmapSource bitmapSource4 = ExtractShellItemIcon(text2);
			if (bitmapSource4 != null)
			{
				return bitmapSource4;
			}
			SHFILEINFO psfi = default(SHFILEINFO);
			SHGetFileInfo(text2, 256u, ref psfi, (uint)Marshal.SizeOf(psfi), 272u);
			if (psfi.hIcon != IntPtr.Zero)
			{
				try
				{
					BitmapSource bitmapSource5 = Imaging.CreateBitmapSourceFromHIcon(psfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
					((Freezable)bitmapSource5).Freeze();
					return bitmapSource5;
				}
				finally
				{
					DestroyIcon(psfi.hIcon);
				}
			}
		}
		catch (Exception)
		{
		}
		return null;
	}

	public static BitmapSource? ExtractShellItemIcon(string path, int size = 64)
	{
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		string text = path.Trim().Trim('"');
		List<string> list = new List<string>();
		if (text.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
		{
			list.Add(text);
		}
		else if (text.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
		{
			list.Add(text);
			list.Add("shell:AppsFolder\\" + text);
		}
		else
		{
			list.Add("shell:AppsFolder\\" + text);
			list.Add(text);
		}
		foreach (string item in list)
		{
			try
			{
				if (SHCreateItemFromParsingName(item, IntPtr.Zero, IShellItemImageFactoryGuid, out IShellItemImageFactory ppv) != 0 || ppv == null)
				{
					continue;
				}
				nint phbm = IntPtr.Zero;
				try
				{
					int image = ppv.GetImage(new SIZE(size, size), 0, out phbm);
					if (image != 0 || phbm == IntPtr.Zero)
					{
						image = ppv.GetImage(new SIZE(size, size), 2, out phbm);
					}
					if (image == 0 && phbm != IntPtr.Zero)
					{
						BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(phbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
						((Freezable)bitmapSource).Freeze();
						return bitmapSource;
					}
				}
				finally
				{
					if (phbm != IntPtr.Zero)
					{
						DeleteObject(phbm);
					}
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static BitmapSource? ExtractPureIconFromFile(string filePath, int iconIndex)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		try
		{
			uint num = ExtractIconEx(filePath, iconIndex, out var phiconLarge, out var phiconSmall, 1u);
			if (num != 0 && phiconLarge != IntPtr.Zero)
			{
				try
				{
					BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHIcon(phiconLarge, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
					((Freezable)bitmapSource).Freeze();
					return bitmapSource;
				}
				finally
				{
					DestroyIcon(phiconLarge);
					if (phiconSmall != IntPtr.Zero)
					{
						DestroyIcon(phiconSmall);
					}
				}
			}
			if (num != 0 && phiconSmall != IntPtr.Zero)
			{
				try
				{
					BitmapSource bitmapSource2 = Imaging.CreateBitmapSourceFromHIcon(phiconSmall, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
					((Freezable)bitmapSource2).Freeze();
					return bitmapSource2;
				}
				finally
				{
					DestroyIcon(phiconSmall);
				}
			}
			SHFILEINFO psfi = default(SHFILEINFO);
			SHGetFileInfo(filePath, 0u, ref psfi, (uint)Marshal.SizeOf(psfi), 256u);
			if (psfi.hIcon != IntPtr.Zero)
			{
				try
				{
					BitmapSource bitmapSource3 = Imaging.CreateBitmapSourceFromHIcon(psfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
					((Freezable)bitmapSource3).Freeze();
					return bitmapSource3;
				}
				finally
				{
					DestroyIcon(psfi.hIcon);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	public static string GetCustomIconsDirectory()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarPie", "CustomIcons");
		if (!Directory.Exists(text))
		{
			Directory.CreateDirectory(text);
		}
		return text;
	}

	public static List<CustomIconItem> GetCustomIcons()
	{
		if (_cachedCustomIcons != null)
		{
			return _cachedCustomIcons;
		}
		List<CustomIconItem> list = new List<CustomIconItem>();
		try
		{
			string customIconsDirectory = GetCustomIconsDirectory();
			if (Directory.Exists(customIconsDirectory))
			{
				string[] files = Directory.GetFiles(customIconsDirectory);
				foreach (string text in files)
				{
					string text2 = Path.GetExtension(text).ToLower();
					string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
					string key = "custom:" + fileNameWithoutExtension;
					switch (text2)
					{
					case ".svg":
						try
						{
							string svgData = ExtractSvgPathData(File.ReadAllText(text));
							list.Add(new CustomIconItem
							{
								Key = key,
								DisplayName = fileNameWithoutExtension,
								FilePath = text,
								SvgData = svgData
							});
						}
						catch
						{
						}
						break;
					case ".png":
					case ".jpg":
					case ".jpeg":
					case ".ico":
					case ".bmp":
					case ".webp":
						list.Add(new CustomIconItem
						{
							Key = key,
							DisplayName = fileNameWithoutExtension,
							FilePath = text,
							SvgData = ""
						});
						break;
					}
				}
			}
		}
		catch (Exception)
		{
		}
		_cachedCustomIcons = list;
		return list;
	}

	public static string ExtractSvgPathData(string svgContent)
	{
		if (string.IsNullOrWhiteSpace(svgContent))
		{
			return "";
		}
		if (svgContent.Trim().StartsWith("M", StringComparison.OrdinalIgnoreCase) && !svgContent.Contains("<svg", StringComparison.OrdinalIgnoreCase))
		{
			return svgContent.Trim();
		}
		try
		{
			List<string> list = new List<string>();
			int num = 0;
			while (num < svgContent.Length)
			{
				int num2 = svgContent.IndexOf(" d=", num, StringComparison.OrdinalIgnoreCase);
				if (num2 < 0)
				{
					num2 = svgContent.IndexOf("\nd=", num, StringComparison.OrdinalIgnoreCase);
				}
				if (num2 < 0)
				{
					num2 = svgContent.IndexOf("\td=", num, StringComparison.OrdinalIgnoreCase);
				}
				if (num2 < 0)
				{
					break;
				}
				int i;
				for (i = num2 + 3; i < svgContent.Length && (svgContent[i] == ' ' || svgContent[i] == '\t'); i++)
				{
				}
				if (i < svgContent.Length && (svgContent[i] == '"' || svgContent[i] == '\''))
				{
					char value = svgContent[i];
					int num3 = i + 1;
					int num4 = svgContent.IndexOf(value, num3);
					if (num4 > num3)
					{
						string text = svgContent.Substring(num3, num4 - num3).Trim();
						if (!string.IsNullOrEmpty(text))
						{
							list.Add(text);
						}
						num = num4 + 1;
						continue;
					}
				}
				num = num2 + 3;
			}
			if (list.Count > 0)
			{
				return string.Join(" ", list);
			}
		}
		catch
		{
		}
		return "";
	}

	public static CustomIconItem? ImportCustomIcon(string sourceFilePath, string? customName = null)
	{
		if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
		{
			return null;
		}
		try
		{
			string customIconsDirectory = GetCustomIconsDirectory();
			string value = Path.GetExtension(sourceFilePath).ToLower();
			string text = (string.IsNullOrWhiteSpace(customName) ? Path.GetFileNameWithoutExtension(sourceFilePath) : customName.Trim());
			char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
			foreach (char oldChar in invalidFileNameChars)
			{
				text = text.Replace(oldChar, '_');
			}
			string path = $"{text}_{DateTime.Now:yyyyMMddHHmmss}{value}";
			string targetPath = Path.Combine(customIconsDirectory, path);
			File.Copy(sourceFilePath, targetPath, overwrite: true);
			_cachedCustomIcons = null;
			return GetCustomIcons().FirstOrDefault((CustomIconItem customIconItem) => customIconItem.FilePath == targetPath);
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static CustomIconItem? ImportCustomSvgData(string svgPathData, string iconName)
	{
		if (string.IsNullOrWhiteSpace(svgPathData))
		{
			return null;
		}
		try
		{
			string customIconsDirectory = GetCustomIconsDirectory();
			string text = (string.IsNullOrWhiteSpace(iconName) ? "Vector" : iconName.Trim());
			char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
			foreach (char oldChar in invalidFileNameChars)
			{
				text = text.Replace(oldChar, '_');
			}
			string path = $"{text}_{DateTime.Now:yyyyMMddHHmmss}.svg";
			string targetPath = Path.Combine(customIconsDirectory, path);
			File.WriteAllText(targetPath, svgPathData.Trim());
			_cachedCustomIcons = null;
			return GetCustomIcons().FirstOrDefault((CustomIconItem customIconItem) => customIconItem.FilePath == targetPath);
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static bool DeleteCustomIcon(string key)
	{
		try
		{
			CustomIconItem customIconItem = GetCustomIcons().FirstOrDefault((CustomIconItem i) => i.Key == key);
			if (customIconItem != null && File.Exists(customIconItem.FilePath))
			{
				File.Delete(customIconItem.FilePath);
				_cachedCustomIcons = null;
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	public static void ClearCache()
	{
		_cachedCustomIcons = null;
		TrimDynamicCache();
	}

	public static ImageSource? GetCustomImageSource(string iconKeyOrPath)
	{
		if (string.IsNullOrWhiteSpace(iconKeyOrPath))
		{
			return null;
		}
		try
		{
			string text = iconKeyOrPath;
			if (iconKeyOrPath.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
			{
				CustomIconItem customIconItem = GetCustomIcons().FirstOrDefault((CustomIconItem i) => string.Equals(i.Key, iconKeyOrPath, StringComparison.OrdinalIgnoreCase));
				if (customIconItem != null)
				{
					text = customIconItem.FilePath;
				}
			}
			if (File.Exists(text) && Path.GetExtension(text).ToLower() != ".svg")
			{
				string cacheKey = "custom_img:" + text;
				if (_pinnedIcons.TryGetValue(cacheKey, out var pinned))
				{
					return pinned;
				}
				if (_dynamicIcons.TryGetValue(cacheKey, out var cached))
				{
					return cached;
				}
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.UriSource = new Uri(text, UriKind.Absolute);
				bitmapImage.EndInit();
				((Freezable)bitmapImage).Freeze();
				CacheDynamicIcon(cacheKey, bitmapImage);
				return bitmapImage;
			}
		}
		catch
		{
		}
		return null;
	}
}

