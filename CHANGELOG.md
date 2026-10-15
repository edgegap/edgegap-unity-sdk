# Edgegap Matchmaking SDK Changelog

Please refer to our [Github repository](https://github.com/edgegap/edgegap-unity-sdk/releases) for up-to-date changelog.

## Unreleased

### Fixed

- `SafeHttpRequest.Post` on Unity versions older than 2022.3 sent the JSON body form-urlencoded, so every matchmaker POST (e.g. creating a group ticket) failed with HTTP 400. The body is now sent as raw UTF-8 JSON.
