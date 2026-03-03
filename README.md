
#A voice-powered sticky notes app for Windows 10+, built with .NET 9.0 WPF.

## Features
- 🎙️ Record your voice → AI transcribes & fixes spelling/pronunciation
- 📝 Up to 5 sticky windows simultaneously (configurable)
- 💾 Notes auto-saved per-day into folders
- 🤖 Supports OpenAI (Whisper + GPT) and Google Gemini
- ⚙️ Settings stored in `%AppData%\mystickymonologues\appsettings.json`

## Color Theme
| Element    | Color     |
|------------|-----------|
| Background | `#001a13` |
| Title      | `#8A9A5B` |
| Controls   | `#3F48CC` |
| Text       | `#FFFFFF`  |

## Getting Started

### Prerequisites
- Windows 10 or later
- .NET 9.0 Runtime
- Microphone

### Build
```
dotnet restore
dotnet build -c Release
dotnet run
```

### First Run
1. Launch the app — a sticky window appears
2. Click the **⚙ gear icon** (visible until setup is complete) or just click the 🎙 mic
3. Fill in your name, email, and AI provider API key
4. Choose **OpenAI** or **Google Gemini** as your AI provider
5. Click **Save Settings**

### Using the App
- **+** (top left) → Open a new sticky window (max 5)
- **🎙** (center) → Start recording; press **⏹ Stop** to transcribe
- **⚙** (top right, only before setup) → Open settings
- **✕** (top right) → Close this sticky window

### Logo
Place your logo at: `logos\final logo.png` relative to the executable.

### Notes Storage
Notes are saved at:
```
Documents\MyStickyMonologues\YYYY-MM-DD\note_<windowid>.txt
```
(Configurable in settings)

## AI Provider Notes
| Provider | Transcription | Text Fix |
|----------|--------------|----------|
| OpenAI   | Whisper API  | GPT-4o-mini (or your model) |
| Gemini   | Gemini 1.5 Flash multimodal | Same call |

## Configuration File
`%AppData%\mystickymonologues\appsettings.json`:
```json
{
  "AppSettings": {
    "MaxWindows": 5,
    "NotesFolder": "C:\\Users\\You\\Documents\\MyStickyMonologues",
    "IsSetupComplete": true,
    "UserName": "Your Name",
    "UserEmail": "you@example.com",
    "AIProvider": "OpenAI",
    "AIApiKey": "sk-...",
    "AIKeyFilePath": "",
    "AIModel": "gpt-4o-mini"
  }
}
```
>>>>>>> e345d4a (Initial commit)
