# LUCY Voice Pipeline Troubleshooting Guide

## Quick Start

```powershell
# Test everything first
.\test-voice-pipeline.ps1

# Or run directly
cd "d:\.NET\LUCY AI\LUCY AI\LUCY AI"
dotnet run
```

## Expected Behavior

### On Startup
1. Window appears with hologram
2. You **HEAR**: "Good [morning/afternoon/evening] BOSS. Lucy is online and listening."
3. Subtitle shows the greeting
4. Ready to accept voice commands

### On Voice Command
1. You say: "Lucy" or "Open Chrome"
2. Subtitle shows: "USER: lucy" or "USER: open chrome"
3. You **HEAR**: Response like "Good morning Boss..." or "Opening Chrome, Boss."
4. Action executes (if applicable)
5. Returns to listening

---

## Common Issues & Fixes

### 🔴 PROBLEM: No voice at all (silent)

**Symptoms:**
- Startup greeting doesn't speak
- Commands show in subtitle but no voice response

**Check:**
```powershell
# Check logs
Get-Content "Logs\lucy-*.log" -Tail 50
```

**Look for:**
- ❌ `"CRITICAL: Failed to initialise SpeechSynthesizer"` → TTS broken
- ❌ `"No TTS engine available"` → Voice engine not created
- ✓ `"TTS engine ready"` → TTS initialized correctly
- ✓ `"TTS speaking: ..."` → TTS was called
- ❌ `"SpeechSynthesizer error during Speak"` → TTS crash

**Fix:**
1. **Test TTS directly:**
   ```powershell
   .\test-tts.ps1
   ```
   If this fails, Windows Speech is broken

2. **Check Windows voice installed:**
   - Open: Settings → Time & Language → Speech
   - Verify "Microsoft Zira Desktop" is installed
   - Test: Click "Preview" button

3. **Check volume not muted:**
   - System volume > 0
   - App volume not muted (Volume Mixer)

---

### 🔴 PROBLEM: LUCY doesn't hear me

**Symptoms:**
- No subtitle appears when speaking
- Logs show `"Voice listening started"` but no commands

**Check:**
```powershell
# Check logs
Get-Content "Logs\lucy-*.log" -Tail 50 | Select-String "STT"
```

**Look for:**
- ❌ `"STT rejected (low confidence)"` → Increase confidence threshold
- ❌ `"STT rejected (audio too quiet)"` → Speak louder or lower threshold
- ✓ `"STT accepted: ..."` → Recognition working

**Fix:**

1. **Check microphone:**
   - Settings → System → Sound → Input
   - Speak and watch the blue bar move
   - If no movement, mic is OFF or wrong device

2. **Lower STT confidence** (if rejecting too much):
   Edit `appsettings.json`:
   ```json
   "SttConfidenceThreshold": 0.3  // was 0.4
   ```

3. **Lower audio level gate** (if speech too quiet):
   Edit `appsettings.json`:
   ```json
   "MinAudioLevelThreshold": 5  // was 10
   ```

4. **Check microphone permissions:**
   - Settings → Privacy → Microphone
   - Allow desktop apps to access microphone

---

### 🔴 PROBLEM: Commands recognized but wrong response

**Symptoms:**
- Subtitle shows correct command
- LUCY says wrong thing or generic response

**Check logs:**
```powershell
Get-Content "Logs\lucy-*.log" -Tail 50 | Select-String "command"
```

**Look for:**
```
[INF] Processing voice command: 'open chrome'
[INF] Generated response: 'Opening Chrome, Boss.'
[INF] TTS speaking: 'Opening Chrome, Boss.'
```

**If response is wrong:**
- Command pattern not matching
- Add more keywords to MainViewModel.cs

---

### 🔴 PROBLEM: LUCY speaks but command doesn't execute

**Symptoms:**
- LUCY says "Opening Chrome" but Chrome doesn't open
- Voice response correct, action fails

**Check:**
```powershell
Get-Content "Logs\lucy-*.log" -Tail 50
```

**Fix:**
- Check WindowsAutomationService.cs
- Application may not be installed
- Path may be incorrect

---

### 🔴 PROBLEM: Startup greeting doesn't speak

**Specific fix for startup:**

**Check logs:**
```powershell
Get-Content "Logs\lucy-*.log" -Tail 100 | Select-String "startup|greeting"
```

**Look for:**
```
[INF] Starting LUCY startup sequence...
[INF] Running voice diagnostics...
[INF] Speaking startup greeting...
[ERR] CRITICAL: Failed to speak startup greeting
```

**If TTS fails:**
1. Check `"TTS engine ready"` appears in logs
2. If missing, TTS initialization failed
3. Check Windows Speech services running

---

## Log Patterns (What's Normal)

### ✅ WORKING (Healthy logs):
```
[INF] ═══════════════════════════════════════════════════
[INF]   LUCY AI — JARVIS Desktop Assistant  (.NET 10)
[INF] ═══════════════════════════════════════════════════
[INF] Configuration sections bound: Application, Ollama, Voice
[INF] Registering core services...
[INF] ✓ VoiceEngineService resolved successfully
[INF] ✓ SoundEffectService resolved successfully
[INF] Initializing TTS engine...
[INF] TTS engine ready — voice: Microsoft Zira Desktop, rate: 0, vol: 100
[INF] Voice listening started.
[INF] Processing voice command: 'lucy'
[INF] Command matched: Greeting
[INF] Generated response: 'Good morning Boss...'
[INF] TTS speaking: 'Good morning Boss...'
[DBG] TTS completed successfully via SpeechSynthesizer
[DBG] Returned to LISTENING state
```

### ❌ BROKEN (Error patterns):
```
[ERR] CRITICAL: Failed to initialise SpeechSynthesizer
[ERR] CRITICAL: No TTS engine available
[ERR] CRITICAL ERROR in voice command processing
[ERR] Failed to resolve required services from DI container
```

---

## Configuration Reference

### appsettings.json Voice Section

```json
"Voice": {
  "SttConfidenceThreshold": 0.4,     // 0.3-0.6 recommended
  "MinAudioLevelThreshold": 10,      // 5-20 recommended
  "SttDebounceMs": 1000,             // 800-1500 recommended
  "VoiceVolume": 100,                // 0-100
  "SpeechRate": 0                    // -10 to +10
}
```

**Adjustments:**

| Symptom | Setting | Change |
|---------|---------|--------|
| Too many false positives | SttConfidenceThreshold | 0.4 → 0.6 |
| Missing valid commands | SttConfidenceThreshold | 0.4 → 0.3 |
| Background noise triggers | MinAudioLevelThreshold | 10 → 15 |
| Quiet speech rejected | MinAudioLevelThreshold | 10 → 5 |
| Commands fire twice | SttDebounceMs | 1000 → 1500 |
| Speech too slow | SpeechRate | 0 → 2 |
| Speech too fast | SpeechRate | 0 → -2 |
| Voice too quiet | VoiceVolume | 100 → 100 (max) |

---

## Test Commands

### Basic Tests (Try First)
1. **"Lucy"** → Should greet you
2. **"What time is it"** → Should tell time
3. **"Open Calculator"** → Should open Calculator
4. **"Volume up"** → Should increase volume

### Advanced Tests
- **"Search Python tutorial"** → Opens Google search
- **"Open Chrome"** → Opens Chrome browser
- **"How are you"** → Status check
- **"Who are you"** → Identity response

### Gujarati Commands (if you speak Gujarati)
- **"Status shu che"** → System status
- **"Chrome kholo"** → Opens Chrome

---

## Emergency Reset

If nothing works:

1. **Reset configuration to safe defaults:**
   ```json
   "Voice": {
     "SttConfidenceThreshold": 0.3,
     "MinAudioLevelThreshold": 5,
     "SttDebounceMs": 1000
   }
   ```

2. **Clean build:**
   ```powershell
   dotnet clean
   dotnet build
   ```

3. **Delete logs and restart:**
   ```powershell
   Remove-Item Logs\*.log
   dotnet run
   ```

4. **Test TTS separately:**
   ```powershell
   .\test-tts.ps1
   ```

---

## Getting Help

When reporting issues, provide:

1. **Last 100 lines of log:**
   ```powershell
   Get-Content "Logs\lucy-*.log" -Tail 100 | Out-File debug.txt
   ```

2. **Your appsettings.json Voice section**

3. **What you said vs what LUCY did:**
   - You: "open chrome"
   - Subtitle: "open chrome"
   - LUCY: (silent / wrong response / correct)

4. **Test results:**
   ```powershell
   .\test-voice-pipeline.ps1 > test-results.txt
   ```

---

## Files Modified in Voice Pipeline Fix

1. **VoiceEngineService.cs** - Enhanced TTS logging
2. **MainViewModel.cs** - Added error handling to all event handlers
3. **App.xaml.cs** - Added service resolution verification
4. **AppSettings.cs** - Added MinAudioLevelThreshold property
5. **appsettings.json** - Updated voice configuration defaults

All changes focused on:
- Comprehensive logging at every step
- Graceful error handling and recovery
- Better default configuration values
- Service initialization verification
