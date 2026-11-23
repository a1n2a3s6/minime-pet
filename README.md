<a id="readme-top"></a>

<!-- HERO ANIMATED BANNER -->
<div align="center">
  <img src="https://capsule-render.vercel.app/api?type=waving&color=0:111827,50:10B981,100:0EA5E9&height=230&section=header&text=MiniMe%20Pet&fontSize=58&fontColor=ffffff&fontAlignY=38&desc=Your%20Chaotic%20Miniature%20Self%20Living%20on%20Windows%20(C%23%20.NET%208%20%2B%20Win32%20API)&descSize=17&descAlignY=63&animation=fadeIn" width="100%" alt="MiniMe Pet Banner" />

  <!-- ANIMATED TYPING TAGLINE -->
  <a href="https://github.com/a1n2a3s6/minime-pet">
    <img src="https://readme-typing-svg.demolab.com?font=JetBrains+Mono&weight=600&size=17&duration=2600&pause=900&color=10B981&center=true&vCenter=true&width=760&lines=Turn+YOURSELF+into+a+chaotic+Windows+Desktop+Buddy+%F0%9F%91%BE;Built-in+AI+Sprite+Prompt+%2B+Chroma+Key+%2300FF00+Auto-Slicer+%E2%9C%82%EF%B8%8F;Climbs+your+windows%2C+shoves+apps+%26+steals+your+mouse+via+Win32+API+%F0%9F%98%88;Roasts+you+in+Egyptian+Arabic+%26+English+%E2%80%94+Offline+or+Gemini%2FGroq+AI+%F0%9F%92%AC" alt="MiniMe Typing Demo" />
  </a>

  <br />

  <!-- TECH & PLATFORM BADGES -->
  <p>
    <img src="https://img.shields.io/badge/365%20Days%20Challenge-Day%2011%2F365-10B981?style=for-the-badge" alt="Day 11/365" />
    <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Windows 10/11" />
    <img src="https://img.shields.io/badge/C%23-.NET%208%20WPF-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt="C# .NET 8 WPF" />
    <img src="https://img.shields.io/badge/Win32-Native%20API-0F172A?style=for-the-badge" alt="Win32 API" />
    <img src="https://img.shields.io/badge/RAM-~35MB%20Ultra--Light-059669?style=for-the-badge" alt="Ultra Light RAM" />
  </p>

  <!-- LIVE GITHUB REPOSITORY STATS -->
  <p>
    <a href="https://github.com/a1n2a3s6/minime-pet/stargazers">
      <img src="https://img.shields.io/github/stars/a1n2a3s6/minime-pet?style=for-the-badge&color=F59E0B&logo=github" alt="GitHub Stars" />
    </a>
    <a href="https://github.com/a1n2a3s6/minime-pet/releases">
      <img src="https://img.shields.io/github/v/release/a1n2a3s6/minime-pet?style=for-the-badge&color=10B981&logo=github" alt="Latest Release" />
    </a>
    <a href="https://github.com/a1n2a3s6/minime-pet/commits/main">
      <img src="https://img.shields.io/github/last-commit/a1n2a3s6/minime-pet?style=for-the-badge&color=06B6D4&logo=git&logoColor=white" alt="Last Commit" />
    </a>
    <img src="https://img.shields.io/badge/License-MIT-6366F1?style=for-the-badge" alt="MIT License" />
  </p>

  <!-- QUICK NAVIGATION BAR -->
  <p>
    <a href="#-how-the-custom-skin-wizard-works"><strong>🎨 Custom Skin Setup</strong></a> &nbsp;│&nbsp;
    <a href="#-desktop-chaos--win32-powers"><strong>😈 Chaos Powers</strong></a> &nbsp;│&nbsp;
    <a href="#-architecture"><strong>🏗️ Architecture</strong></a> &nbsp;│&nbsp;
    <a href="#-download--install"><strong>📦 Download .EXE</strong></a> &nbsp;│&nbsp;
    <a href="#-shortcuts--controls"><strong>⌨️ Controls</strong></a>
  </p>
</div>

---

> [!IMPORTANT]
> **Zero heavy 3D GPUs. Zero cloud subscriptions. Pure native Windows chaos.**
> **MiniMe Pet** is an ultra-lightweight **C# (.NET 8 + WPF)** Desktop Buddy that lives always-on-top over every Windows app, interacts with your real desktop windows via **Win32 API**, roasts you based on what you're doing (in Egyptian Arabic or English), and lets you turn **your own photo** into a custom animated 8-frame skin in 60 seconds using our built-in **AI Prompt Generator + Chroma Key (`#00FF00`) Auto-Slicer**.

---

## 🎨 How the Custom Skin Wizard Works

Instead of bloating the app with gigabytes of local AI models, **MiniMe** separates image generation from runtime performance:

1. **Pick Your Style:** Choose **Chibi Anime**, **Pixel Art**, or **2D Cartoon** inside the Setup Wizard.
2. **Copy the Magic Prompt:** MiniMe gives you a precision-engineered prompt requesting a **4×2 Sprite Sheet (8 poses)** on a pure `#00FF00` green screen. Paste it with your selfie into any AI image generator.
3. **Upload & Auto-Slice:** Drop the image into MiniMe — the built-in **Chroma Key Engine** strips the green screen cleanly (with spill suppression) and slices all 8 frames automatically!

| Row | Col 1 | Col 2 | Col 3 | Col 4 |
| :---: | :---: | :---: | :---: | :---: |
| **Row 1** | 🧍 `Idle` | 🚶 `Walk 1` | 🏃 `Walk 2` | 😈 `Troll / Laugh` |
| **Row 2** | 💫 `Hit / Dizzy` | 🤏 `Dragged` | 🪑 `Sitting` | 😴 `Sleeping` |

> [!TIP]
> Don't have a custom sprite sheet yet? **MiniMe ships with a built-in animated Chibi character** so you can launch and enjoy it immediately out of the box!

---

## 😈 Desktop Chaos & Win32 Powers

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>🧠 Context-Aware Trolling</h3>
      <p>Reads your active foreground window (<code>GetForegroundWindow</code>). Coding in VS Code for hours? It tells you to drink water. Procrastinating on YouTube? It calls you out in hilarious <b>Egyptian Arabic</b> or English speech bubbles.</p>
    </td>
    <td width="50%" valign="top">
      <h3>🧗 Real Window Climber</h3>
      <p>Locates open non-maximized windows via <code>GetWindowRect</code>, leaps up onto their titlebar, and sits there dangling its legs while watching you work.</p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>💥 Win32 Chaos Mode (Toggleable)</h3>
      <p>Physically shoves/shakes your open windows (<code>SetWindowPos</code>), chases and playfully steals your mouse cursor for a second (<code>SetCursorPos</code>), or sneakily minimizes a window and hides.</p>
    </td>
    <td width="50%" valign="top">
      <h3>🏀 Real 2D Slap &amp; Throw Physics</h3>
      <p>Click fast to <b>slap</b> MiniMe across the screen with gravity and taskbar bounce (complete with spinning dizzy stars!), or grab it by the collar and <b>fling</b> it anywhere.</p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>🤖 Offline Quips + Optional AI Chat</h3>
      <p>Works 100% offline with a rich categorized JSON quip pack — or plug in a free <b>Gemini Flash</b> / <b>Groq</b> API key to unlock live context-aware roasts and double-click mini-bubble chat!</p>
    </td>
    <td width="50%" valign="top">
      <h3>🤫 Instant Boss / Focus Mode</h3>
      <p>Press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>H</kbd> from any app or game to put MiniMe to sleep in the corner and pause all Chaos actions instantly.</p>
    </td>
  </tr>
</table>

---

## 🏗️ Architecture

```mermaid
flowchart LR
    subgraph Setup["🎨 Skin Pipeline (One-Time Setup)"]
        Prompt["AI Prompt Generator\n(Chibi • Pixel • Cartoon)"] --> Img["User Uploads 4x2 PNG\n(#00FF00 Green Screen)"]
        Img --> Chroma["Chroma Key Remover\n+ Green Spill Suppression"]
        Chroma --> Slicer["4x2 Auto-Slicer\n(8 Animated Frames)"]
    end

    subgraph Runtime["⚡ .NET 8 WPF Runtime (~35MB RAM)"]
        Loop["60fps Physics & State Loop"]
        Win32["🪟 Native Win32 Interop\n(user32.dll P/Invoke)"]
        Brain["🧠 BrainService\n(Offline JSON / Gemini / Groq)"]
        Overlay["👻 Always-On-Top Transparent Overlay"]
    end

    Slicer --> Overlay
    Loop --> Overlay
    Win32 <--> Loop
    Win32 --> Brain
    Brain --> Overlay
```

<details>
<summary><b>📂 Click to expand full C# .NET 8 project structure</b></summary>

```text
minime-pet/
├── .github/
│   └── workflows/
│       └── release.yml                # Automated single-file Windows .exe build
├── src/
│   └── MiniMe/
│       ├── MiniMe.csproj              # .NET 8 WPF + WinForms Tray (Zero external NuGet deps)
│       ├── App.xaml / App.xaml.cs     # System Tray icon & Ctrl+Shift+H global hotkey
│       ├── Native/
│       │   └── Win32Interop.cs        # P/Invoke bindings for user32.dll
│       ├── Engine/
│       │   ├── SpriteProcessor.cs     # Chroma Key remover, 4x2 slicer & fallback sprite
│       │   ├── PhysicsEngine.cs       # Gravity, bounce, slap launch & fling velocity
│       │   ├── ChaosEngine.cs         # Window Climber, Pusher, Mouse Thief & Minimizer
│       │   └── BrainService.cs        # Context-aware offline quips + Gemini/Groq API
│       ├── Models/
│       │   ├── PetConfig.cs
│       │   └── PetState.cs
│       ├── Assets/
│       │   └── quips.json             # Categorized Egyptian Arabic & English quips
│       └── Views/
│           ├── PetWindow.xaml(.cs)    # Transparent Always-On-Top pet & speech bubble
│           ├── SetupWindow.xaml(.cs)  # AI Prompt + Chroma Key Auto-Slicer Wizard
│           └── SettingsWindow.xaml(.cs)
└── README.md
```

</details>

---

## 📦 Download & Install

### Option 1: Standalone Windows `.exe` (Zero Install)
Download **`minime-windows-x64.exe`** directly from **[GitHub Releases](https://github.com/a1n2a3s6/minime-pet/releases)** and double-click to run!

### Option 2: Build from Source (.NET 8 SDK)
```powershell
git clone [https://github.com/a1n2a3s6/minime-pet.git](https://github.com/a1n2a3s6/minime-pet.git)
cd minime-pet
dotnet run --project src/MiniMe/MiniMe.csproj
```

---

## ⌨️ Shortcuts & Controls

| Interaction | Action |
| :---: | :--- |
| **Single Quick Click** | Slap MiniMe (launches with gravity, dizzy stars & sarcastic reply) |
| **Click & Drag** | Pick MiniMe up by the collar and fling across the screen |
| **Double Click** | Open mini chat bubble (AI Mode) or trigger a fresh context roast |
| **Right Click / Tray Icon** | Open Skin Setup Wizard, Settings, or Summon to Center |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>H</kbd> | **Boss / Focus Mode** — instant sleep & pause all Chaos |

---

## 🗺️ Roadmap

- [x] Native C# .NET 8 WPF transparent Always-On-Top pet with 60fps throw/slap physics
- [x] Built-in AI 4×2 Sprite Prompt Generator + Chroma Key (`#00FF00`) Auto-Slicer
- [x] Win32 API Window Climber, Window Pusher, Mouse Thief & Context-Aware Trolling
- [x] Offline Egyptian Arabic / English quips + Optional Gemini Flash / Groq AI chat
- [x] Global `Ctrl+Shift+H` Boss Mode + Automated single-file `.exe` GitHub Release
- [ ] Multi-Pet Mode (spawn 2 MiniMes that argue with each other on your taskbar)
- [ ] Optional retro sound effects pack (slap, boing, giggle)
- [ ] Community Skin Marketplace (export/import `.minime` skin packs)

---

## 📄 License

Distributed under the **MIT License**. Free to use, customize, and share!

---

<div align="center">

<h3>👨‍💻 Built by Anas Alhelaly</h3>
<p><em>Day 11 of my <strong>365 Projects in 365 Days</strong> GitHub Challenge</em></p>

<p>
  <a href="https://anas-alhelaly.vercel.app/">
    <img src="https://img.shields.io/badge/Portfolio-anas--alhelaly.vercel.app-000000?style=for-the-badge&logo=vercel&logoColor=white" alt="Portfolio" />
  </a>
  <a href="https://github.com/a1n2a3s6">
    <img src="https://img.shields.io/badge/GitHub-a1n2a3s6-181717?style=for-the-badge&logo=github&logoColor=white" alt="GitHub" />
  </a>
  <a href="https://www.instagram.com/anas.alhelaly/">
    <img src="https://img.shields.io/badge/Instagram-@anas.alhelaly-E4405F?style=for-the-badge&logo=instagram&logoColor=white" alt="Instagram" />
  </a>
</p>

<p>⭐ <strong>If MiniMe made you laugh (or stole your mouse), drop a Star on the repo!</strong> ⭐</p>

<p><a href="#readme-top"><strong>⬆️ Back to Top</strong></a></p>

<!-- ANIMATED WAVE FOOTER -->
<img src="https://capsule-render.vercel.app/api?type=waving&color=0:0EA5E9,50:10B981,100:111827&height=110&section=footer" width="100%" alt="Footer Wave" />

</div>
