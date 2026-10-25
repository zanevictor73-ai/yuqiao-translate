<div align="center">

# Yuqiao · 语桥

### Type Chinese. English or Japanese appears in Claude. Read replies in Chinese alongside it.

A two-way translation companion for Claude on Windows.<br>
**Automatic draft filling · Side-by-side translated replies · No copy-paste loop**

[简体中文](README.md) · **English**

[Download for Windows](https://github.com/zanevictor73-ai/yuqiao-translate/releases/latest) · [Quick start](#quick-start) · [Compatibility](#compatibility)

![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?style=flat-square)
![License: MIT](https://img.shields.io/badge/License-MIT-75c8b8?style=flat-square)
![No LLM key](https://img.shields.io/badge/LLM%20API%20key-not%20required-6654c7?style=flat-square)

</div>

Write or dictate Chinese in Yuqiao, and **your chosen English or Japanese translation appears directly in Claude's message box**. After you send it, Claude's English or Japanese reply is **translated automatically in the Chinese reading panel on the right**, together with your original Chinese question.

**Connect what you want to say with what comes back.** Keep the conversation moving without shuttling text between a translator and a chat window.

![Illustrated Claude workflow: Chinese input, English or Japanese draft filling, and Chinese replies alongside the conversation](docs/workflow.svg)

No large language model, model credits, or LLM API key to configure. Translation uses conventional Microsoft / Google online machine translation: **an internet connection is required; this is not an offline translator.**

## Claude edition: a connected conversation workflow

**Designed around the complete Claude Windows desktop workflow: composing, reading replies, navigating the sidebar and understanding settings.** Supported behavior includes:

- **Speak Chinese, choose English or Japanese.** Select the outgoing language in the input window. Translation begins after an input pause and fills the selected result into Claude, using the corresponding punctuation.
- **You choose when to send.** Translation and filling alone never send. Your explicit Enter requests sending; IME candidate confirmation does not. Shift + Enter inserts a line break.
- **Start the next thought cleanly.** The local draft clears only after the corresponding user message is observed in Claude, preventing the next dictation from appending to the previous sentence.
- **Keep the caret where it belongs.** After filling, focus returns to Claude for Enter and Backspace. Click the Chinese input to edit it; dictation keeps focus locally.
- **Read at your own pace.** Your Chinese question remains in the reader, followed by the translated answer. Incoming answer updates do not drag you to the last line.
- **Understand the interface.** A companion panel translates visible sidebar, profile-menu and settings labels into Chinese without modifying Claude.

“Complete workflow” refers to these listed features. It is **not a guarantee for every Claude version or every screen**: application updates, layout changes, window sizes and accessibility information can affect integration.

## Choose an edition

| | **Claude edition · Main experience** | General edition |
|---|---|---|
| Core experience | Chinese input → English or Japanese in Claude; replies → Chinese on the right | Independent text translation |
| Voice | Windows voice typing → translation → automatic filling | Windows voice typing → translation |
| Reading and interface | Your Chinese originals + translated replies; independent floating panels | Dark window, always-on-top and copy controls |
| Languages | Chinese → English / Japanese automatic filling; English / Japanese replies → Chinese | Chinese → Japanese + English; Japanese / English → Chinese |
| Download | **Yuqiao-Claude-v1.10.0-windows.zip** | **Yuqiao-General-v1.10.0-windows.zip** |

General works independently when an application has no integration; it does not read or control other apps. Both are portable, manually launched applications. Neither includes automatic startup, a background launch watcher, a control dashboard, or changes to the system time zone.

## Quick start

1. Choose an edition on the [Releases page](https://github.com/zanevictor73-ai/yuqiao-translate/releases/latest), then **extract the entire ZIP**.
2. Open **Yuqiao.General.exe** or **Yuqiao.Claude.exe**. No Python, Node.js or local language model installation is needed.
3. In General, select a language direction and type. In Claude, first open Claude for Windows, select “Claude 客户端”, enable automatic translation / filling, select English or Japanese under “发送语言” (Outgoing language), and enter floating mode when needed.

For speech, select a Chinese Windows input method, then click “语音输入” (Voice input). Recognition is provided by **Windows + H voice typing**. End dictation from the Windows panel. Availability depends on the system language, microphone and online speech settings.

Before sending, review the translated draft. Edit the Chinese source, review alternatives, or edit the English or Japanese directly in Claude. The companion avoids overwriting a draft it does not own.

## Compatibility

| Environment | Status |
|---|---|
| Windows 11 x64 | Local build, automated checks and desktop validation environment |
| Windows 10 with .NET Framework 4.8 | Intended runtime; not separately verified on a physical machine |
| Claude for Windows | Primary integration, with the workflow listed above |
| Claude website | Automatic following and filling are not formally supported; General remains usable as a separate translator |
| macOS / Linux / Windows ARM | Unverified; no supported package or compatibility claim |

This release presents only Claude for Windows as its validated conversation integration. Other AI applications without verified end-to-end workflows are not advertised as supported. The interface is primarily Chinese; README and quick-start documentation are bilingual. This independent project is not affiliated with Anthropic, Microsoft or Google.

## How it works, and where text goes

The Claude edition uses Windows **UI Automation**, the operating system's accessibility interface, to read visible text from the selected application and track conversation identity. Conventional translation services convert text; Windows interfaces handle placement and filling. No LLM is required to interpret or operate the screen.

- **Online processing:** text goes to Microsoft, with Google as a fallback. Explicit alternative Japanese candidates also use Google. Windows dictation may use Microsoft's online services according to your system settings.
- **No saved chat files:** drafts, translations and recent conversation indexes remain in memory. The Claude edition saves only app selection, outgoing language and panel preferences in the local application-data folder.
- **Diagnostics are off by default:** opt-in diagnostics contain operational fields such as type, text length, timing and provider, not original text, translations or audio.
- **Latency varies:** translations aim to appear within a few seconds after an input pause, but 3–4 seconds is not guaranteed. Public service endpoints may be limited, changed or withdrawn; permanent free access and availability are not promised.

See [Architecture & validation](docs/ARCHITECTURE.md) for implementation and test details.

## Build from source

Requires Windows, .NET Framework 4.8 and PowerShell. Uses the system C# compiler with no additional package dependencies.

~~~powershell
.\build.ps1
.\tests\Run-Tests.ps1
.\scripts\Package.ps1 -SkipBuild
~~~

Executables are written to dist/; distributable ZIP files go to release/. Build one edition with **-Edition General** or **-Edition Claude**.

There are **224 automated checks**: 203 for the Claude workflow and 21 for General. They use synthetic text and simulated services and do not send real chat messages. Live service availability, speech recognition accuracy and future application versions still require real-world validation.

## Contribute

Bug reports, adapter improvements and translation-workflow ideas are welcome. Include your Windows version, target app version, edition and reproduction steps. Remove personal information and private conversations from screenshots first.

Released under the [MIT License](LICENSE). The software license does not replace third-party translation-service terms.
