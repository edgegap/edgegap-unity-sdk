# Edgegap Unity SDK

This SDK is an optional starter kit for Unity users, which can be extended and customized later.
- [Getting Started With Matchmaking](https://docs.edgegap.com/unity/matchmaking)
- [Getting Started With Server Browser](https://docs.edgegap.com/unity/server-browser) (video coming soon)

[![Watch the video](https://img.youtube.com/vi/HxtvzvJ1FTk/0.jpg)](https://www.youtube.com/watch?v=HxtvzvJ1FTk)

This plugin has been tested, and supports Unity versions 2021.3.0f1+, including Unity 6 LTS. This plugin is provided 100% free of charge, under Terms and Conditions of Free Tier.

## Requirements

Install a Git Client, for example [git-scm](https://git-scm.com/). A git client is needed for Unity to download and install this package automatically. You will not need to use git directly once it's installed.

## Installation

1. Open your Unity project,
2. Select toolbar option **Window** -> **Package Management** -> **Package Manager**,
3. Click the **+** icon and select **Add package from git URL...**,
4. Input the URL of our SDK when prompted:

```
https://github.com/edgegap/edgegap-unity-sdk.git
```

5. Click **Add** and wait until installation is completed.

## Verified Sources

Besides this repository:
- [OpenUPM source](https://openupm.com/packages/com.edgegap.unity-sdk/).

Do not trust unverified sources!

## Update Package

Navigate to Edgegap SDK in Unity Package Manager and click **Update**.

> [!WARNING]
> **Imported Samples are not updated automatically!** Back up any custom property values, delete the sample scripts used in your scene currently and re-import samples.

> [!NOTE]
> Some releases may contain breaking changes. This will be indicated by a new MAJOR version.

## Troubleshooting

> Unity Editor shows `[Package Manager Window] Error adding package: https://github.com/edgegap/edgegap-unity-matchmaking-sdk.git`

- If you’re adding our plugin via git URL, you will need to have a git client installed.

> Unity Editor 2021 shows `failed to resolve assembly: 'Edgegap.Matchmaking.SDK...`

- This is a known issue when using plugin with [Unity's Burst compiler](https://docs.unity3d.com/6000.0/Documentation/Manual/com.unity.burst.html).
- Install plugin [via ZIP archive](#install-via-zip-archive) and delete `EdgegapMatchmakingSDK.asmdef` in the plugin folder to resolve this.

> Visual Studio shows `type or namespace name could not be found` for Edgegap namespace.

1. In your Unity Editor, navigate to **Edit / Preferences / External Tools / Generate .csproj files**.
2. Make sure you have enabled **Git packages**.
3. Click **Regenerate project files**.

## For Plugin Developers

This section is only for developers working on this plugin or other plugins interacting / integrating this plugin.

### CSharpier Code Frmatter

This project uses [CSharpier code formatter](https://csharpier.com/) to ensure consistent and readable formatting, configured in `/.config/dotnet-tools.json`.

See [Editor integration](https://csharpier.com/docs/Editors) for Visual Studio extensions, optionally configure `Reformat with CSharpier` on Save under Tools | Options | CSharpier | General. You may also configure [running formatting as a pre-commit git hook](https://csharpier.com/docs/Pre-commit).
