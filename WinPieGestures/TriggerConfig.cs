namespace WinPieGestures;

public class TriggerConfig
{
	/// <summary>逐属性深拷贝，供程序专属配置以全局触发键为起点初始化、及 profile Clone 复制使用。</summary>
	public TriggerConfig Clone()
	{
		return new TriggerConfig
		{
			TriggerType = this.TriggerType,
			MouseButton = this.MouseButton,
			Key = this.Key,
			VkCode = this.VkCode,
			RequireCtrl = this.RequireCtrl,
			RequireShift = this.RequireShift,
			RequireAlt = this.RequireAlt,
			RequireWin = this.RequireWin,
			DisplayText = this.DisplayText
		};
	}

	public string TriggerType { get; set; } = "Mouse";

	public string MouseButton { get; set; } = "RightButton";

	public string Key { get; set; } = "None";

	public uint VkCode { get; set; }

	public bool RequireCtrl { get; set; }

	public bool RequireShift { get; set; }

	public bool RequireAlt { get; set; }

	public bool RequireWin { get; set; }

	public string DisplayText { get; set; } = "\ud83d\uddb1\ufe0f 鼠标右键 (Right Button)";
}
