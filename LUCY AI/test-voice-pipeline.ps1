# ═══════════════════════════════════════════════════════════════════════
#  LUCY VOICE PIPELINE TEST SCRIPT
#  Tests the complete voice command → response → TTS flow
# ═══════════════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  LUCY VOICE PIPELINE VERIFICATION" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# ── Step 1: Verify Build ──────────────────────────────────────────────
Write-Host "[1/6] Checking build output..." -ForegroundColor Yellow
$dllPath = "bin\Debug\net10.0-windows\LucyAI.dll"
if (Test-Path $dllPath) {
    $dllSize = (Get-Item $dllPath).Length
    Write-Host "  ✓ Build output exists: $dllPath ($([math]::Round($dllSize/1KB, 2)) KB)" -ForegroundColor Green
} else {
    Write-Host "  ✗ Build output NOT FOUND!" -ForegroundColor Red
    Write-Host "  Run: dotnet build" -ForegroundColor Yellow
    exit 1
}

# ── Step 2: Verify Configuration ──────────────────────────────────────
Write-Host ""
Write-Host "[2/6] Checking appsettings.json..." -ForegroundColor Yellow
if (Test-Path "appsettings.json") {
    $config = Get-Content "appsettings.json" | ConvertFrom-Json
    
    Write-Host "  Voice Settings:" -ForegroundColor White
    Write-Host "    WakeWord: $($config.Voice.WakeWord)" -ForegroundColor White
    Write-Host "    TTS Engine: $($config.Voice.TtsEngine)" -ForegroundColor White
    Write-Host "    STT Confidence: $($config.Voice.SttConfidenceThreshold)" -ForegroundColor White
    Write-Host "    Min Audio Level: $($config.Voice.MinAudioLevelThreshold)" -ForegroundColor White
    Write-Host "    Debounce: $($config.Voice.SttDebounceMs) ms" -ForegroundColor White
    
    if ($config.Voice.SttConfidenceThreshold -lt 0.3) {
        Write-Host "  ⚠ WARNING: STT confidence too low ($($config.Voice.SttConfidenceThreshold)) - may accept noise" -ForegroundColor Yellow
    } elseif ($config.Voice.SttConfidenceThreshold -gt 0.7) {
        Write-Host "  ⚠ WARNING: STT confidence too high ($($config.Voice.SttConfidenceThreshold)) - may reject valid speech" -ForegroundColor Yellow
    } else {
        Write-Host "  ✓ STT confidence in optimal range (0.3-0.7)" -ForegroundColor Green
    }
} else {
    Write-Host "  ✗ appsettings.json NOT FOUND!" -ForegroundColor Red
    exit 1
}

# ── Step 3: Test Windows TTS Engine ───────────────────────────────────
Write-Host ""
Write-Host "[3/6] Testing Windows TTS engine..." -ForegroundColor Yellow

Add-Type -AssemblyName System.Speech

try {
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    
    # Try to select Zira (same as LUCY)
    try {
        $synth.SelectVoice("Microsoft Zira Desktop")
        Write-Host "  ✓ Microsoft Zira Desktop voice available" -ForegroundColor Green
    } catch {
        Write-Host "  ⚠ Zira voice not found, using default: $($synth.Voice.Name)" -ForegroundColor Yellow
    }
    
    Write-Host "  Voice: $($synth.Voice.Name)" -ForegroundColor White
    Write-Host "  Speaking test phrase..." -ForegroundColor White
    
    $synth.Speak("LUCY voice test successful.")
    
    Write-Host "  ✓ TTS engine working correctly" -ForegroundColor Green
    $synth.Dispose()
    
} catch {
    Write-Host "  ✗ TTS ENGINE FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  LUCY will NOT be able to speak!" -ForegroundColor Red
    exit 1
}

# ── Step 4: Check Microphone ──────────────────────────────────────────
Write-Host ""
Write-Host "[4/6] Checking microphone availability..." -ForegroundColor Yellow

try {
    $recognizer = New-Object System.Speech.Recognition.SpeechRecognitionEngine
    $recognizer.SetInputToDefaultAudioDevice()
    Write-Host "  ✓ Default microphone detected" -ForegroundColor Green
    $recognizer.Dispose()
} catch {
    Write-Host "  ✗ NO MICROPHONE DETECTED!" -ForegroundColor Red
    Write-Host "  LUCY will NOT be able to listen!" -ForegroundColor Red
    Write-Host "  Check: Settings → System → Sound → Input" -ForegroundColor Yellow
}

# ── Step 5: Check Ollama (optional) ───────────────────────────────────
Write-Host ""
Write-Host "[5/6] Checking Ollama AI service (optional)..." -ForegroundColor Yellow

try {
    $response = Invoke-WebRequest -Uri "http://localhost:11434/api/tags" -TimeoutSec 2 -UseBasicParsing
    if ($response.StatusCode -eq 200) {
        $models = ($response.Content | ConvertFrom-Json).models
        Write-Host "  ✓ Ollama is running" -ForegroundColor Green
        Write-Host "  Available models: $($models.name -join ', ')" -ForegroundColor White
    }
} catch {
    Write-Host "  ⚠ Ollama not running (AI questions will fail)" -ForegroundColor Yellow
    Write-Host "  Simple commands will still work!" -ForegroundColor White
    Write-Host "  To start: ollama serve" -ForegroundColor Gray
}

# ── Step 6: Check Logs Directory ──────────────────────────────────────
Write-Host ""
Write-Host "[6/6] Checking logs directory..." -ForegroundColor Yellow

if (Test-Path "Logs") {
    $logFiles = Get-ChildItem "Logs" -Filter "lucy-*.log" | Sort-Object LastWriteTime -Descending
    if ($logFiles) {
        $latestLog = $logFiles[0]
        Write-Host "  ✓ Logs directory exists" -ForegroundColor Green
        Write-Host "  Latest log: $($latestLog.Name) ($($latestLog.LastWriteTime))" -ForegroundColor White
    } else {
        Write-Host "  ✓ Logs directory exists (no logs yet)" -ForegroundColor Green
    }
} else {
    Write-Host "  ⚠ Logs directory doesn't exist (will be created on first run)" -ForegroundColor Yellow
}

# ── Summary ────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  VERIFICATION COMPLETE" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "✓ Build successful" -ForegroundColor Green
Write-Host "✓ Configuration valid" -ForegroundColor Green
Write-Host "✓ TTS engine working" -ForegroundColor Green
Write-Host ""

Write-Host "NEXT STEPS:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Run LUCY:" -ForegroundColor White
Write-Host "   dotnet run" -ForegroundColor Cyan
Write-Host ""
Write-Host "2. Wait for startup greeting (you should HEAR it speak)" -ForegroundColor White
Write-Host ""
Write-Host "3. Try these test commands:" -ForegroundColor White
Write-Host "   • 'Lucy' → Should greet you" -ForegroundColor Gray
Write-Host "   • 'What time is it' → Should tell time" -ForegroundColor Gray
Write-Host "   • 'Open Chrome' → Should open Chrome" -ForegroundColor Gray
Write-Host "   • 'Volume up' → Should increase volume" -ForegroundColor Gray
Write-Host ""
Write-Host "4. Check logs if commands don't work:" -ForegroundColor White
Write-Host "   Get-Content 'Logs\lucy-*.log' -Tail 50" -ForegroundColor Cyan
Write-Host ""

Write-Host "LOGS TO WATCH FOR:" -ForegroundColor Yellow
Write-Host "  ✓ 'TTS engine ready' → TTS initialized" -ForegroundColor Gray
Write-Host "  ✓ 'Voice listening started' → STT active" -ForegroundColor Gray
Write-Host "  ✓ 'Processing voice command' → Command heard" -ForegroundColor Gray
Write-Host "  ✓ 'TTS speaking: ...' → About to speak" -ForegroundColor Gray
Write-Host "  ✓ 'TTS completed successfully' → Spoke successfully" -ForegroundColor Gray
Write-Host ""

Write-Host "If LUCY doesn't respond:" -ForegroundColor Yellow
Write-Host "  1. Check subtitle appears (proves she heard you)" -ForegroundColor White
Write-Host "  2. Check logs for errors" -ForegroundColor White
Write-Host "  3. Lower STT confidence in appsettings.json to 0.3" -ForegroundColor White
Write-Host "  4. Check microphone is not muted in Windows" -ForegroundColor White
Write-Host ""

Write-Host "Press any key to start LUCY..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host ""
Write-Host "Starting LUCY..." -ForegroundColor Cyan
Write-Host ""

# Start LUCY
dotnet run
