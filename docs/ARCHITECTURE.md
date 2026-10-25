# 架构与验证 / Architecture & validation

## 两个构建目标 / Two build targets

Claude 版使用 C#、WPF 和 Windows UI Automation。通用版仅编译独立窗口、机器翻译、标点整理、Windows 听写入口及单实例管理；其二进制中不包含聊天读取或自动填入实现。两版共用翻译服务，不依赖大模型、浏览器扩展或第三方 NuGet 包。

The Claude edition uses C#, WPF and Windows UI Automation. General compiles only its independent window, translation service, punctuation handling, Windows dictation entry and single-instance logic. Its binary excludes chat readers and composers. Both share a translation service and require no LLM, browser extension or third-party NuGet package.

| Source | Responsibility / 职责 |
|---|---|
| GeneralApp.cs | Independent translation window, debounce, cancellation, stale-result protection |
| App.cs, FloatingViews.cs, PanelControls.cs | Claude companion UI and independently controlled floating panels |
| TranslationService.cs, TranslationPunctuation.cs | Microsoft primary, Google fallback, bounded memory cache, target-language punctuation |
| AutomaticDraft.cs, ClaudeComposer.cs | Draft ownership, automatic English/Japanese filling, conflict protection |
| ConversationFlow.cs, ClaudeSend.cs, ConversationUi.cs | Explicit send requests, verification and clearing, original Chinese history |
| ConversationLanguage.cs | Outgoing language selection, independent result eligibility and incoming English/Japanese detection |
| ConversationReadingPosition.cs | Stable reading anchor during reply updates |
| ChatWindowReader.cs, ClaudeChromeReader.cs, ClaudeSettingsReader.cs | Conversation identity and visible Claude UI extraction |
| NavigationTranslator.cs, NavigationViews.cs | Local UI vocabulary plus translated conversation titles |
| VoiceInputService.cs | Windows + H request; no recording or custom recognition engine |
| AppRuntime.cs | One manually launched instance per edition and Windows session |

## 数据边界 / Data boundaries

- Online destinations: edge.microsoft.com and translate.googleapis.com. Translation requests carry the requested text. Google fallback uses an HTTPS query containing text.
- Windows voice typing is controlled by Windows and may use Microsoft's cloud services. The application does not record audio or save audio files.
- Drafts, translations, recent chat indexes and service caches are memory-only. They are not encrypted memory.
- Claude preferences are stored in the current user's LocalApplicationData / YuqiaoTranslateStandalone folder: selected app, outgoing language and panel visibility only.
- Optional diagnostic output contains event kind, text length, timing and provider, not chat text. Diagnostics are disabled by default.
- Fixed UI vocabulary is translated locally; visible conversation titles may be sent to the translation service when navigation translation is enabled.
- No telemetry collector, model keys, browser credentials, automatic-start registration or system time-zone changes are implemented in these public editions.

在线请求包含待翻译文字。聊天只在内存保留，不等于加密内存或离线处理。菜单里的固定词汇在本地映射，可见会话标题可能联网翻译。Windows 听写遵循系统设置。公开源码和压缩包不包含私人配置、真实聊天或运行日志。

## 验证 / Validation

Local test environment: Windows 11 x64, .NET Framework 4.8 compiler/runtime. Windows 10, ARM, macOS and Linux have not been separately verified.

| Check group | Count | Coverage |
|---|---:|---|
| ConversationV19Probe | 37 | Explicit send, verified clearing, repeated questions, focus-related state, panel control |
| AutoFillV18Probe | 31 | Ownership, stale input, cancellation, failed lanes and conflict preservation |
| SettingsVoiceV19Probe | 24 | Settings labels and bounds, Windows dictation shortcut wiring |
| UiStateProbe | 31 | Conversation changes, pause/reconnect, initial chat migration and UI state |
| FloatingViewsProbe | 15 | Floating layout, input/read/navigation views |
| FloatingStateProbe | 14 | Panel state and disconnected behavior |
| ReadingScrollV191Probe | 22 | Question anchor, manual scrolling and streamed answer changes |
| EnglishConversationProbe | 29 | English automatic filling, explicit sending, receipt clearing, mixed reply detection, language changes, failure isolation and saved preferences |
| GeneralEditionProbe | 21 | Build separation, language direction, debounce, stale results, pause, IME, copy eligibility |
| **Total** | **224** | **Synthetic, deterministic checks against the built binaries** |

These tests do not send real chat messages, use the microphone or contact translation services. Windows desktop smoke testing separately verified Chinese → Japanese + English and Japanese → Chinese using synthetic phrases. On 2026-10-25, real desktop testing on this Windows 11 machine verified Chinese → English automatic filling → explicit Enter → English reply translated to Chinese, followed by the same flow in Japanese. Both cleared the local Chinese draft after verified sending and retained the original Chinese in the reading panel. The recorded desktop run used the personal build sharing the same translation, composer and reading code; the public build is separately checked by the suites above. Automated checks are not proof of speech recognition accuracy or compatibility with every target-app version.

自动检查不调用麦克风、不向实际聊天发送消息，也不访问翻译服务。联网冒烟验证另外使用了合成短句。2026-10-25 的真实桌面演示验证了英文和日文的自动填入、主动回车发送、草稿清空及右侧中文回译。实机录屏使用共享上述核心代码的自用构建，公开构建另外通过上述自动检查。没有把自动检查当作所有软件版本均兼容的证据。

## 服务来源说明 / Service attribution

The endpoint used by the Microsoft adapter was identified while reviewing [KISS Translator's Microsoft adapter](https://github.com/fishjar/kiss-translator/blob/dev/src/builtin-plugins/microsoft.js). Yuqiao's C# implementation was written independently; it does not bundle that project's code. Public client endpoints may change and are not a service-level guarantee. Respect the providers' terms.

MIT covers this project's source and documentation. Target applications, Windows features and translation services retain their own terms.
