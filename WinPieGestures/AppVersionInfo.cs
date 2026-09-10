using System;
using System.Reflection;

namespace WinPieGestures;

/// <summary>提供用于界面、日志和托盘的统一应用版本文本。</summary>
internal static class AppVersionInfo
{
	private const string FallbackVersion = "1.7.3-beta.5";

	public static string DisplayVersion { get; } = ResolveDisplayVersion();

	public static string DisplayVersionWithPrefix => "v" + DisplayVersion;

	private static string ResolveDisplayVersion()
	{
		Assembly assembly = typeof(AppVersionInfo).Assembly;
		string? informationalVersion = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
			.InformationalVersion;

		if (!string.IsNullOrWhiteSpace(informationalVersion))
		{
			// SDK 可能附加“+提交哈希”；用户界面只显示语义版本及预发布标识。
			int metadataSeparator = informationalVersion.IndexOf('+');
			string displayVersion = metadataSeparator >= 0
				? informationalVersion[..metadataSeparator]
				: informationalVersion;

			displayVersion = displayVersion.Trim();
			if (displayVersion.StartsWith('v') || displayVersion.StartsWith('V'))
			{
				displayVersion = displayVersion[1..];
			}

			if (!string.IsNullOrWhiteSpace(displayVersion))
			{
				return displayVersion;
			}
		}

		return assembly.GetName().Version?.ToString(3) ?? FallbackVersion;
	}
}
