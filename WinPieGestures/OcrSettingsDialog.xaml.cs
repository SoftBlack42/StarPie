using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace WinPieGestures;

public partial class OcrSettingsDialog : Window
{
	public OcrSettingsDialog()
	{
		InitializeComponent();
	}

	private void Window_Loaded(object sender, RoutedEventArgs e)
	{
		LoadConfig();
	}

	private void LoadConfig()
	{
		OcrSettings cfg = ConfigManager.CurrentConfig?.OcrConfig ?? new OcrSettings();

		string provider = cfg.Provider ?? "Local";
		if (provider == "Ai") ProviderAiRadio.IsChecked = true;
		else if (provider == "Custom") ProviderCustomRadio.IsChecked = true;
		else ProviderLocalRadio.IsChecked = true;

		UpdateProviderVisibility();

		// Local
		SetComboSelectedTag(LocalLangComboBox, cfg.LocalLanguage ?? "zh-Hans");

		// AI
		AiEndpointTextBox.Text = !string.IsNullOrEmpty(cfg.AiEndpoint) ? cfg.AiEndpoint : "https://api.openai.com/v1";
		AiApiKeyBox.Password = cfg.AiApiKey ?? "";
		AiModelTextBox.Text = !string.IsNullOrEmpty(cfg.AiModel) ? cfg.AiModel : "gpt-4o-mini";
		SetComboSelectedTag(AiPromptModeComboBox, cfg.AiPromptMode ?? "text");

		// Custom
		CustomHttpUrlTextBox.Text = !string.IsNullOrEmpty(cfg.CustomHttpUrl) ? cfg.CustomHttpUrl : "http://127.0.0.1:1224/api/ocr";

		// Behaviors
		AutoCopyCheckBox.IsChecked = cfg.AutoCopyToClipboard;
		ShowResultWinCheckBox.IsChecked = cfg.ShowResultWindow;
		RemoveCjkSpacesCheckBox.IsChecked = cfg.RemoveSpacesBetweenCjk;
		MergeLinesCheckBox.IsChecked = cfg.MergeLines;

		try
		{
			if (OcrEngine.AvailableRecognizerLanguages.Count == 0)
			{
				LocalLangStatusAlertBorder.Visibility = Visibility.Visible;
				LocalLangStatusAlertText.Text = "⚠️ 系统未检测到本地 OCR 语言包。建议安装「光学字符识别」可选功能，或切换至上方「AI 视觉模型」。";
			}
			else
			{
				LocalLangStatusAlertBorder.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
			LocalLangStatusAlertBorder.Visibility = Visibility.Collapsed;
		}
	}

	private void OpenOptionalFeaturesButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Process.Start(new ProcessStartInfo { FileName = "ms-settings:optionalfeatures", UseShellExecute = true });
		}
		catch
		{
		}
	}

	private void ProviderRadio_Checked(object sender, RoutedEventArgs e)
	{
		UpdateProviderVisibility();
	}

	private void UpdateProviderVisibility()
	{
		if (LocalEngineSettingsPanel == null) return;

		bool isLocal = ProviderLocalRadio.IsChecked == true;
		bool isAi = ProviderAiRadio.IsChecked == true;
		bool isCustom = ProviderCustomRadio.IsChecked == true;

		LocalEngineSettingsPanel.Visibility = isLocal ? Visibility.Visible : Visibility.Collapsed;
		AiEngineSettingsPanel.Visibility = isAi ? Visibility.Visible : Visibility.Collapsed;
		CustomEngineSettingsPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
	}

	private void AiModelPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (AiModelPresetComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
		{
			AiModelTextBox.Text = item.Tag.ToString();
		}
	}

	private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
	{
		TestResultLabel.Text = "⏳ 测试中...";
		TestResultLabel.Foreground = System.Windows.Media.Brushes.Yellow;

		try
		{
			if (ProviderLocalRadio.IsChecked == true)
			{
				string langTag = GetComboSelectedTag(LocalLangComboBox) ?? "zh-Hans";
				bool supported = OcrEngine.IsLanguageSupported(new Language(langTag));
				if (supported)
				{
					TestResultLabel.Text = "✓ 本地语言包已就绪，支持原生极速识别";
					TestResultLabel.Foreground = System.Windows.Media.Brushes.LightGreen;
				}
				else
				{
					int count = OcrEngine.AvailableRecognizerLanguages.Count;
					TestResultLabel.Text = $"⚠️ 当前语言 [{langTag}] 未安装，可用语言包数: {count}";
					TestResultLabel.Foreground = System.Windows.Media.Brushes.Orange;
				}
			}
			else if (ProviderAiRadio.IsChecked == true)
			{
				string ep = AiEndpointTextBox.Text.Trim();
				using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
				if (!string.IsNullOrWhiteSpace(AiApiKeyBox.Password))
				{
					client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AiApiKeyBox.Password.Trim());
				}
				using HttpResponseMessage resp = await client.GetAsync(ep.TrimEnd('/') + "/models");
				if (resp.IsSuccessStatusCode || (int)resp.StatusCode == 401 || (int)resp.StatusCode == 400)
				{
					TestResultLabel.Text = $"✓ 接口端点连通正常 (HTTP {(int)resp.StatusCode})";
					TestResultLabel.Foreground = System.Windows.Media.Brushes.LightGreen;
				}
				else
				{
					TestResultLabel.Text = $"⚠️ 端点响应异常 (HTTP {(int)resp.StatusCode})";
					TestResultLabel.Foreground = System.Windows.Media.Brushes.Orange;
				}
			}
			else
			{
				string url = CustomHttpUrlTextBox.Text.Trim();
				using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
				using HttpResponseMessage resp = await client.GetAsync(url);
				TestResultLabel.Text = $"✓ 微服务已连通 (HTTP {(int)resp.StatusCode})";
				TestResultLabel.Foreground = System.Windows.Media.Brushes.LightGreen;
			}
		}
		catch (Exception ex)
		{
			TestResultLabel.Text = $"✕ 连通失败: {ex.Message}";
			TestResultLabel.Foreground = System.Windows.Media.Brushes.Salmon;
		}
	}

	private void TestSnippetButton_Click(object sender, RoutedEventArgs e)
	{
		SaveConfigValues();
		OcrManager.StartCaptureAndRecognize();
	}

	private void SaveButton_Click(object sender, RoutedEventArgs e)
	{
		SaveConfigValues();
		ConfigManager.SaveConfig();
		DialogResult = true;
		Close();
	}

	private void SaveConfigValues()
	{
		if (ConfigManager.CurrentConfig == null) return;
		ConfigManager.CurrentConfig.OcrConfig ??= new OcrSettings();
		OcrSettings cfg = ConfigManager.CurrentConfig.OcrConfig;

		if (ProviderAiRadio.IsChecked == true) cfg.Provider = "Ai";
		else if (ProviderCustomRadio.IsChecked == true) cfg.Provider = "Custom";
		else cfg.Provider = "Local";

		cfg.LocalLanguage = GetComboSelectedTag(LocalLangComboBox) ?? "zh-Hans";
		cfg.AiEndpoint = AiEndpointTextBox.Text.Trim();
		cfg.AiApiKey = AiApiKeyBox.Password.Trim();
		cfg.AiModel = AiModelTextBox.Text.Trim();
		cfg.AiPromptMode = GetComboSelectedTag(AiPromptModeComboBox) ?? "text";
		cfg.CustomHttpUrl = CustomHttpUrlTextBox.Text.Trim();

		cfg.AutoCopyToClipboard = AutoCopyCheckBox.IsChecked == true;
		cfg.ShowResultWindow = ShowResultWinCheckBox.IsChecked == true;
		cfg.RemoveSpacesBetweenCjk = RemoveCjkSpacesCheckBox.IsChecked == true;
		cfg.MergeLines = MergeLinesCheckBox.IsChecked == true;
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}

	private void Window_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			Close();
		}
	}

	private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private static void SetComboSelectedTag(System.Windows.Controls.ComboBox cb, string tag)
	{
		if (cb == null) return;
		foreach (ComboBoxItem item in cb.Items)
		{
			if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
			{
				cb.SelectedItem = item;
				return;
			}
		}
	}

	private static string? GetComboSelectedTag(System.Windows.Controls.ComboBox cb)
	{
		return (cb?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
	}
}
