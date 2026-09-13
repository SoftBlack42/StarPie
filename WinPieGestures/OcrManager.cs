using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WinPieGestures;

public static class OcrManager
{
	private static readonly HttpClient s_httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };

	/// <summary>打开 OCR 多引擎与接口设置弹窗</summary>
	public static void ShowSettingsDialog()
	{
		Application.Current?.Dispatcher.Invoke(() =>
		{
			try
			{
				OcrSettingsDialog dlg = new OcrSettingsDialog();
				dlg.Owner = Application.Current?.MainWindow;
				dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
				dlg.ShowDialog();
			}
			catch (Exception ex)
			{
				AppLogger.LogError("Failed to show OcrSettingsDialog", ex);
			}
		});
	}

	/// <summary>由动作触发：全屏框选截屏并执行 OCR 文本提取</summary>
	public static void StartCaptureAndRecognize()
	{
		Application.Current?.Dispatcher.Invoke(() =>
		{
			try
			{
				ScreenSnipWindow snip = new ScreenSnipWindow(async bmp =>
				{
					if (bmp != null)
					{
						await ProcessSnippetAsync(bmp);
					}
				});
				snip.Show();
			}
			catch (Exception ex)
			{
				AppLogger.LogError("Failed to launch ScreenSnipWindow", ex);
				MessageBox.Show("启动截屏框选失败: " + ex.Message, "StarPie", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		});
	}

	public static async Task ProcessSnippetAsync(Bitmap bmp)
	{
		OcrSettings config = ConfigManager.CurrentConfig?.OcrConfig ?? new OcrSettings();
		Stopwatch sw = Stopwatch.StartNew();
		string recognizedText = "";
		string engineName = "本地离线引擎";

		Bitmap workingBmp = bmp;

		try
		{
			// 尺寸与超分辨率自适应准备（防止 2600px 溢出崩溃，提升微小字号清晰度）
			// 放在 try/finally 内，确保预处理自身失败时原始截图也能被释放。
			workingBmp = PrepareBitmapForOcr(bmp);

			string provider = config.Provider?.Trim() ?? "Local";
			switch (provider)
			{
			case "Ai":
				engineName = $"AI 视觉大模型 ({config.AiModel})";
				recognizedText = await RecognizeWithAiVisionAsync(workingBmp, config);
				break;

			case "Custom":
				engineName = "自定义 HTTP OCR";
				recognizedText = await RecognizeWithCustomHttpAsync(workingBmp, config);
				break;

			case "Cloud":
				engineName = $"{config.CloudProvider} 云端 OCR";
				recognizedText = await RecognizeWithCloudAsync(workingBmp, config);
				break;

			case "Local":
			default:
				engineName = "Windows 本地离线引擎";
				recognizedText = await RecognizeWithLocalWinRtAsync(workingBmp, config);
				break;
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("OCR recognition error", ex);
			recognizedText = $"[OCR 识别异常]: {ex.Message}";
		}
		finally
		{
			sw.Stop();
			if (!ReferenceEquals(workingBmp, bmp))
			{
				try { workingBmp.Dispose(); } catch { }
			}
			try { bmp.Dispose(); } catch { }
		}

		string latency = $"{sw.ElapsedMilliseconds}ms";

		// 格式后处理：去除中文字符与全角标点间硬塞的空格
		if (!recognizedText.StartsWith("["))
		{
			recognizedText = PostProcessText(recognizedText, config.RemoveSpacesBetweenCjk);
		}

		// 调度回 UI 线程分发结果
		Application.Current?.Dispatcher.Invoke(() =>
		{
			if (!string.IsNullOrWhiteSpace(recognizedText) && config.AutoCopyToClipboard)
			{
				try
				{
					System.Windows.Clipboard.SetText(recognizedText);
				}
				catch
				{
				}
			}

			if (config.ShowResultWindow)
			{
				OcrResultWindow resWin = new OcrResultWindow(recognizedText, engineName, latency);
				resWin.Show();
			}

			if (config.SearchInBrowser && !string.IsNullOrWhiteSpace(recognizedText))
			{
				try
				{
					string q = recognizedText.Trim();
					if (q.Length > 80) q = q.Substring(0, 80);
					string url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(q);
					Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
				}
				catch
				{
				}
			}

			if (!config.ShowResultWindow)
			{
				_ = Task.Run(async () =>
				{
					try
					{
						await Task.Delay(5000).ConfigureAwait(false);
						MemoryOptimizer.TrimMemory(force: false);
					}
					catch { }
				});
			}
		});
	}

	/// <summary>1. Windows 10/11 原生 Windows.Media.Ocr 引擎</summary>
	private static async Task<string> RecognizeWithLocalWinRtAsync(Bitmap bmp, OcrSettings config)
	{
		if (OcrEngine.AvailableRecognizerLanguages.Count == 0)
		{
			return "[提示]: 当前 Windows 系统未检测到本地原生 OCR 识别引擎组件。\n（常见于精简版/企业版 Windows 系统，或系统尚未下载「光学字符识别」可选功能）\n\n💡 推荐解决方案：\n1. 【一键切换到 AI 视觉大模型】（推荐 · 免安装任何本地包 · 识别精度最高）：\n   在 StarPie 接口设置中配置硅基流动 / DeepSeek / OpenAI / Ollama 等端点，支持极速文字、表格与公式提取。\n2. 【安装 Windows 原生 OCR 功能】：\n   打开 Windows 设置 -> 应用 -> 可选功能 -> 添加可选功能，搜索并安装「中文(简体)光学字符识别」即可恢复离线使用。";
		}

		OcrEngine? engine = null;
		string langTag = config.LocalLanguage ?? "zh-Hans";

		try
		{
			if (OcrEngine.IsLanguageSupported(new Language(langTag)))
			{
				engine = OcrEngine.TryCreateFromLanguage(new Language(langTag));
			}
		}
		catch
		{
		}

		if (engine == null)
		{
			try
			{
				engine = OcrEngine.TryCreateFromUserProfileLanguages();
			}
			catch
			{
			}
		}

		if (engine == null)
		{
			engine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
		}

		if (engine == null)
		{
			return "[提示]: 无法初始化本地 OCR 引擎。建议在 StarPie 动作设置中切换为 AI 视觉大模型 / 云端接口。";
		}

		using SoftwareBitmap softwareBitmap = await ConvertToSoftwareBitmapAsync(bmp);
		OcrResult result = await engine.RecognizeAsync(softwareBitmap);
		if (result == null || result.Lines.Count == 0)
		{
			return "[未识别到有效文字内容]";
		}

		return ReconstructLayout(result, config);
	}

	/// <summary>2. OpenAI 兼容 / 本地 Ollama 多模态视觉模型 API</summary>
	private static async Task<string> RecognizeWithAiVisionAsync(Bitmap bmp, OcrSettings config)
	{
		string endpoint = config.AiEndpoint?.Trim() ?? "https://api.openai.com/v1";
		if (!endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
		{
			endpoint = endpoint.TrimEnd('/') + "/chat/completions";
		}

		string base64Image;
		using (MemoryStream ms = new MemoryStream())
		{
			bmp.Save(ms, ImageFormat.Jpeg);
			base64Image = Convert.ToBase64String(ms.ToArray());
		}

		string prompt = config.AiPromptMode switch
		{
			"latex" => "请提取图片中的全部数学公式与文字，将数学公式转换为标准 LaTeX 格式（如 $$...$$ 或 $...$）。仅输出公式与文本，不要包含多余开场白。",
			"markdown" => "请提取图片中的文字与表格结构，将表格转换为标准 Markdown 表格格式。不要包含多余寒暄。",
			"translate" => "请提取图片中的文字并直接翻译为流畅的简体中文。仅输出翻译结果。",
			_ => "请精确提取图片中的全部文字。保持原有行结构，不要包含任何前缀或解释说明。"
		};

		var requestBody = new
		{
			model = string.IsNullOrWhiteSpace(config.AiModel) ? "gpt-4o-mini" : config.AiModel.Trim(),
			messages = new object[]
			{
				new
				{
					role = "user",
					content = new object[]
					{
						new { type = "text", text = prompt },
						new
						{
							type = "image_url",
							image_url = new { url = $"data:image/jpeg;base64,{base64Image}" }
						}
					}
				}
			},
			max_tokens = 2000
		};

		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint);
		if (!string.IsNullOrWhiteSpace(config.AiApiKey))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AiApiKey.Trim());
		}
		request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

		using HttpResponseMessage response = await s_httpClient.SendAsync(request);
		string responseJson = await response.Content.ReadAsStringAsync();

		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {responseJson}");
		}

		using JsonDocument doc = JsonDocument.Parse(responseJson);
		if (doc.RootElement.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
		{
			JsonElement firstChoice = choices[0];
			if (firstChoice.TryGetProperty("message", out JsonElement message) && message.TryGetProperty("content", out JsonElement content))
			{
				return content.GetString()?.Trim() ?? "";
			}
		}

		return responseJson;
	}

	/// <summary>3. 自定义 HTTP 私有化 OCR (PaddleOCR / Umi-OCR)</summary>
	private static async Task<string> RecognizeWithCustomHttpAsync(Bitmap bmp, OcrSettings config)
	{
		string url = config.CustomHttpUrl?.Trim() ?? "http://127.0.0.1:1224/api/ocr";
		using MemoryStream ms = new MemoryStream();
		bmp.Save(ms, ImageFormat.Png);
		string base64 = Convert.ToBase64String(ms.ToArray());

		var requestObj = new { base64 = base64 };
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url)
		{
			Content = new StringContent(JsonSerializer.Serialize(requestObj), Encoding.UTF8, "application/json")
		};

		using HttpResponseMessage resp = await s_httpClient.SendAsync(req);
		string resText = await resp.Content.ReadAsStringAsync();
		if (!resp.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {resText}");
		}

		try
		{
			using JsonDocument doc = JsonDocument.Parse(resText);
			if (doc.RootElement.TryGetProperty("data", out JsonElement data))
			{
				if (data.ValueKind == JsonValueKind.String) return data.GetString() ?? "";
				if (data.ValueKind == JsonValueKind.Array)
				{
					StringBuilder sb = new StringBuilder();
					foreach (var item in data.EnumerateArray())
					{
						if (item.TryGetProperty("text", out JsonElement txt)) sb.AppendLine(txt.GetString());
					}
					return sb.ToString().TrimEnd();
				}
			}
		}
		catch
		{
		}

		return resText;
	}

	/// <summary>4. 商业云端 OCR 占位支持</summary>
	private static async Task<string> RecognizeWithCloudAsync(Bitmap bmp, OcrSettings config)
	{
		await Task.Delay(100);
		return $"[{config.CloudProvider} 云端 OCR]: 凭证已就绪 (可直接在设置中绑定 API Key 与 Secret)";
	}

	/// <summary>
	/// 智能图像尺寸与超分辨率优化：
	/// 1. 约束最大尺寸在 2500px 以内，消除 Windows.Media.Ocr 的 2600px 溢出抛异常崩溃；
	/// 2. 对微小字号/小图（如行高小于 60px）进行高保真双三次插值放大（2x 或 3x），显著提升笔画识别特征。
	/// </summary>
	private static Bitmap PrepareBitmapForOcr(Bitmap input)
	{
		int w = input.Width;
		int h = input.Height;
		const int MaxAllowedDim = 2500;

		// 1. 超大尺寸下采样保护
		if (w > MaxAllowedDim || h > MaxAllowedDim)
		{
			double scale = Math.Min((double)MaxAllowedDim / w, (double)MaxAllowedDim / h);
			int newW = Math.Max(10, (int)Math.Round(w * scale));
			int newH = Math.Max(10, (int)Math.Round(h * scale));

			Bitmap scaled = new Bitmap(newW, newH, PixelFormat.Format24bppRgb);
			using (Graphics g = Graphics.FromImage(scaled))
			{
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
				g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
				g.DrawImage(input, 0, 0, newW, newH);
			}
			return scaled;
		}

		// 2. 极小字号/细微图像超分辨率放大增强
		if (h < 60 || w < 60)
		{
			int scaleFactor = (h < 30 || w < 30) ? 3 : 2;
			int newW = w * scaleFactor;
			int newH = h * scaleFactor;

			if (newW <= MaxAllowedDim && newH <= MaxAllowedDim)
			{
				Bitmap scaled = new Bitmap(newW, newH, PixelFormat.Format24bppRgb);
				using (Graphics g = Graphics.FromImage(scaled))
				{
					g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
					g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
					g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
					g.DrawImage(input, 0, 0, newW, newH);
				}
				return scaled;
			}
		}

		return input;
	}

	private static async Task<SoftwareBitmap> ConvertToSoftwareBitmapAsync(Bitmap bmp)
	{
		using MemoryStream ms = new MemoryStream();
		// 使用标准 BMP 编码保存，彻底规避 GDI+ 32bpp 未初始化 Alpha 黑化问题
		bmp.Save(ms, ImageFormat.Bmp);
		byte[] bytes = ms.ToArray();

		using InMemoryRandomAccessStream ras = new InMemoryRandomAccessStream();
		using (DataWriter writer = new DataWriter(ras))
		{
			writer.WriteBytes(bytes);
			await writer.StoreAsync();
			await writer.FlushAsync();
			writer.DetachStream();
		}
		ras.Seek(0);

		BitmapDecoder decoder = await BitmapDecoder.CreateAsync(ras);
		// 强制忽略 Alpha 通道，保证图像为 100% 不透明实色 RGB，OCR 引擎绝无黑屏风险
		SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
		return softwareBitmap;
	}

	private class LineLayoutInfo
	{
		public double Left { get; set; }
		public double Top { get; set; }
		public double Right { get; set; }
		public double Bottom { get; set; }
		public double Height => Bottom - Top;
		public string FormattedText { get; set; } = "";
	}

	/// <summary>
	/// 基于 OCR 词边界几何空间的版面结构智能重建引擎：
	/// 1. 词间距分析：智能识别表格列对齐并保留制表间距，去除中文汉字与标点间硬塞的空格，保留英文字词与标点规范；
	/// 2. 空间聚类排序：按行高阈值进行水平基线聚类，防止多栏排版混读；
	/// 3. 自然段落还原：行间距大于 0.75 倍行高时自动插入空行；
	/// 4. 智能合并断行：开启 MergeLines 时对同一长句的自动折行平滑拼接，遇到句末标点、缩进或序号列表时保留换行。
	/// </summary>
	private static string ReconstructLayout(OcrResult result, OcrSettings config)
	{
		if (result.Lines == null || result.Lines.Count == 0)
		{
			return "";
		}

		List<LineLayoutInfo> lineInfos = new();
		foreach (var line in result.Lines)
		{
			if (line.Words == null || line.Words.Count == 0)
			{
				if (!string.IsNullOrWhiteSpace(line.Text))
				{
					lineInfos.Add(new LineLayoutInfo
					{
						FormattedText = line.Text.Trim()
					});
				}
				continue;
			}

			double minX = double.MaxValue;
			double minY = double.MaxValue;
			double maxX = double.MinValue;
			double maxY = double.MinValue;

			foreach (var w in line.Words)
			{
				var r = w.BoundingRect;
				if (r.X < minX) minX = r.X;
				if (r.Y < minY) minY = r.Y;
				if (r.X + r.Width > maxX) maxX = r.X + r.Width;
				if (r.Y + r.Height > maxY) maxY = r.Y + r.Height;
			}

			string formatted = FormatLineWords(line.Words, config);
			if (string.IsNullOrWhiteSpace(formatted))
			{
				continue;
			}

			lineInfos.Add(new LineLayoutInfo
			{
				Left = minX,
				Top = minY,
				Right = maxX,
				Bottom = maxY,
				FormattedText = formatted
			});
		}

		if (lineInfos.Count == 0)
		{
			return "";
		}

		// 计算平均行高
		double avgH = lineInfos.Average(l => Math.Max(8.0, l.Height));

		// 行聚类排序：垂直方向在 0.45 * avgH 容差内视为同一视觉行，按 X 从左到右，其余按 Y 严格从上到下
		lineInfos.Sort((a, b) =>
		{
			double diffY = a.Top - b.Top;
			if (Math.Abs(diffY) > avgH * 0.45)
			{
				return a.Top.CompareTo(b.Top);
			}
			return a.Left.CompareTo(b.Left);
		});

		StringBuilder resultSb = new StringBuilder();
		for (int i = 0; i < lineInfos.Count; i++)
		{
			var currLine = lineInfos[i];
			if (i == 0)
			{
				resultSb.Append(currLine.FormattedText);
				continue;
			}

			var prevLine = lineInfos[i - 1];
			double vGap = currLine.Top - prevLine.Bottom;
			double lineH = Math.Max(avgH, Math.Max(currLine.Height, prevLine.Height));

			// 1. 自然段落大间距或空行判断
			if (vGap > lineH * 0.75)
			{
				resultSb.AppendLine();
				resultSb.AppendLine();
				resultSb.Append(currLine.FormattedText);
				continue;
			}

			// 2. 智能合并断行
			if (config.MergeLines)
			{
				string prevText = prevLine.FormattedText;
				char prevLastChar = prevText.Length > 0 ? prevText[^1] : ' ';
				string currText = currLine.FormattedText;
				char currFirstChar = currText.Length > 0 ? currText[0] : ' ';

				bool isPrevSentenceEnd = "。！？…；.!?;\":".IndexOf(prevLastChar) >= 0;
				bool isCurrListOrBullet = IsListOrBulletItem(currText);
				bool isIndented = (currLine.Left - prevLine.Left) > lineH * 1.2;

				if (isPrevSentenceEnd || isCurrListOrBullet || isIndented)
				{
					// 句末完结、列表项目或缩进，保持自然换行
					resultSb.AppendLine();
					resultSb.Append(currLine.FormattedText);
				}
				else
				{
					// 同一句话段内自动折行，平滑拼接
					if (prevText.EndsWith("-") && IsAsciiWordChar(currFirstChar))
					{
						resultSb.Length--; // 移除行尾连字符
						resultSb.Append(currLine.FormattedText);
					}
					else if (IsCjk(prevLastChar) && IsCjk(currFirstChar))
					{
						resultSb.Append(currLine.FormattedText);
					}
					else
					{
						resultSb.Append(' ');
						resultSb.Append(currLine.FormattedText);
					}
				}
			}
			else
			{
				resultSb.AppendLine();
				resultSb.Append(currLine.FormattedText);
			}
		}

		return resultSb.ToString().TrimEnd();
	}

	private static string FormatLineWords(IReadOnlyList<OcrWord> words, OcrSettings config)
	{
		if (words.Count == 0) return "";
		if (words.Count == 1) return words[0].Text?.Trim() ?? "";

		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < words.Count; i++)
		{
			var curr = words[i];
			string currText = curr.Text ?? "";
			if (currText.Length == 0) continue;

			if (i > 0)
			{
				var prev = words[i - 1];
				string prevText = prev.Text ?? "";
				double gap = curr.BoundingRect.X - (prev.BoundingRect.X + prev.BoundingRect.Width);
				double charW = Math.Max(4.0, prev.BoundingRect.Height * 0.65);

				// 1. 水平大间隙保留（表格列或对齐项目）
				if (gap > charW * 2.2)
				{
					int spaces = Math.Min(8, Math.Max(2, (int)Math.Round(gap / charW)));
					sb.Append(new string(' ', spaces));
				}
				else
				{
					char lastChar = prevText.Length > 0 ? prevText[^1] : ' ';
					char firstChar = currText[0];

					bool prevIsCjk = IsCjk(lastChar);
					bool currIsCjk = IsCjk(firstChar);
					bool prevIsPunct = IsFullWidthPunctuation(lastChar);
					bool currIsPunct = IsFullWidthPunctuation(firstChar);

					if (config.RemoveSpacesBetweenCjk && (prevIsCjk || prevIsPunct) && (currIsCjk || currIsPunct))
					{
						// 中文词间及全角标点间消除空格
					}
					else if (config.RemoveSpacesBetweenCjk && (prevIsCjk && IsHalfWidthPunctuation(firstChar) || IsHalfWidthPunctuation(lastChar) && currIsCjk))
					{
						// 汉字与半角标点贴合
					}
					else if ((IsAsciiWordChar(lastChar) && currIsCjk) || (prevIsCjk && IsAsciiWordChar(firstChar)))
					{
						// 中英文词界保留一个微空格
						sb.Append(' ');
					}
					else if (!prevIsCjk && !currIsCjk)
					{
						// 英文/数字词间保留正常空格
						sb.Append(' ');
					}
				}
			}

			sb.Append(currText);
		}

		return sb.ToString().Trim();
	}

	private static bool IsListOrBulletItem(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return false;
		string t = text.TrimStart();
		return Regex.IsMatch(t, @"^(\d+[\.\)、]|[\(（]\d+[\)）]|[•\-\*\+·]|[\u2460-\u2473]|[一二三四五六七八九十]+[、\.\)])");
	}

	private static bool IsCjk(char c)
	{
		return (c >= 0x4E00 && c <= 0x9FFF) ||
		       (c >= 0x3400 && c <= 0x4DBF) ||
		       (c >= 0x3040 && c <= 0x309F) ||
		       (c >= 0x30A0 && c <= 0x30FF) ||
		       (c >= 0xAC00 && c <= 0xD7AF);
	}

	private static bool IsFullWidthPunctuation(char c)
	{
		return "，。！？：；、（）【】《》“”‘’—…·「」『』〈〉".IndexOf(c) >= 0;
	}

	private static bool IsHalfWidthPunctuation(char c)
	{
		return ",.!:;?()[]{}<>\"'".IndexOf(c) >= 0;
	}

	private static bool IsAsciiWordChar(char c)
	{
		return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
	}

	private static string PostProcessText(string text, bool removeCjkSpaces)
	{
		if (string.IsNullOrWhiteSpace(text)) return text;
		string res = text;
		if (removeCjkSpaces)
		{
			// 清除汉字与汉字、汉字与全角标点之间的多余空格
			res = Regex.Replace(res, @"(?<=[\u4e00-\u9fa5，。！？：；、（）【】《》“”‘’])\s+(?=[\u4e00-\u9fa5，。！？：；、（）【】《》“”‘’])", "");
		}
		return res;
	}
}
