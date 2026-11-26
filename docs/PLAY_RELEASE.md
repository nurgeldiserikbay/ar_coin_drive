# Google Play release checklist — AR Coin Drive

Package: `com.arcoindrive.game` · Build: `AR Coin Drive > Build Google Play Bundle` (or batch mode `-executeMethod ARCoinDriveRelease.BuildBundle`) → `build/ARCoinDrive-<version>-<code>.aab`.
Upload key: `../keys/upload.keystore` + `../keys/release.properties` (outside the repo; back it up, never commit). Use Play App Signing.

## Before the release build

1. **AdMob**: done — app `ca-app-pub-9702825788968948~7895231377`, Interstitial `…/1220856863`, Rewarded `…/8784807060` in `GameAds.cs`.
2. **AdMob > Privacy & messaging**: create a **GDPR (European regulations)** message for the app and publish it. Without it the consent form never appears and EEA/UK players get no ads.
3. **Privacy policy**: published at https://docs.google.com/document/d/e/2PACX-1vQC9oA5-f4n4TVwz0G8_WTuk_a2wLmmRno5B0NewYHIg4PUXv_c3BSojrKTB8_GQOxlbXIIXtYoDhcB/pub (also in `GameAds.PrivacyPolicyUrl`).
4. Increase `VersionCode` in `Assets/Editor/ARCoinDriveRelease.cs` for every upload (the Android build profile overrides Player Settings, so the script sets it).
5. After publishing, add `app-ads.txt` to the developer website listed in Play Console (AdMob shows the line).

## Play Console

- **In-app products** (one-time, non-consumable — IDs must match `GameStore.cs`): `vip_pass` (~$3.99, VIP: x2 coins, no ads), `car_stinger` (~$2.99), `car_apex` (~$4.99).
- **Ads**: "Yes, my app contains ads".
- **Advertising ID**: "Yes", purpose: *Advertising or marketing* (the Ads SDK declares `AD_ID`).
- **Target audience**: 13–15, 16–17, 18+ (not under 13). Not in the Families programme. Answer "no" to "appeals to children".
- **Content rating** (IARC questionnaire): category *Game*; cartoon vehicle racing/bumping, no blood, no gambling; in-app purchases yes; ads yes; users interact only on the local network (no chat). Expected rating: Everyone / PEGI 3.
- **Data safety**:
  - Data collected: *Device or other IDs* (advertising ID), *Approximate location* (from IP, by AdMob), *App interactions* and *Diagnostics* (crash logs, performance, by AdMob/Unity). Purposes: Advertising or marketing, Analytics, Fraud prevention/security. Shared with third parties: yes (Google AdMob).
  - *Purchase history*: collected by Google Play, purpose App functionality.
  - Camera images: processed on device only, not collected (ephemeral processing is not "collection").
  - Data encrypted in transit: yes. Users can request deletion: no account data exists; point to device ad settings.
- **Permissions** (from the merged manifest): CAMERA (AR), INTERNET, ACCESS_NETWORK_STATE, VIBRATE, WAKE_LOCK, FOREGROUND_SERVICE, BILLING, `AD_ID` + `ACCESS_ADSERVICES_*` (Ads SDK). minSdk 29, targetSdk 36.
- **App category**: Game > Racing. Tags: AR, RC car.
- **Store listing**: icon 512×512 (`Branding/`), feature graphic 1024×500, 2–8 phone screenshots, short description (≤80 chars), full description.
- **Testing**: a new personal developer account needs a closed test with 12+ testers for 14 days before production access.
