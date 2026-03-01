use std::path::{Path, PathBuf};
use std::process::{Command, Output};
use std::time::{Duration, Instant};

use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use tokio::time::sleep;
use tracing::{debug, info, warn};

use crate::config::Settings;
use crate::tcp::client::TcpClient;

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "lowercase")]
pub enum GameVersion {
    Il2cpp,
    Mono,
}

impl GameVersion {
    pub fn parse(input: &str) -> Result<Self, String> {
        match input.to_lowercase().as_str() {
            "il2cpp" => Ok(Self::Il2cpp),
            "mono" => Ok(Self::Mono),
            _ => Err(format!(
                "Error: Invalid version '{input}'. Must be 'il2cpp' or 'mono'."
            )),
        }
    }

    fn as_display(self) -> &'static str {
        match self {
            Self::Il2cpp => "IL2CPP",
            Self::Mono => "MONO",
        }
    }
}

#[derive(Debug, Clone)]
pub struct LaunchGameOptions {
    pub version: GameVersion,
    pub enable_debugger: bool,
    pub wait_for_connection: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProcessEntry {
    pub pid: i64,
    pub cpu_time: f64,
    pub memory_mb: f64,
    pub start_time: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GameProcessInfo {
    pub running: bool,
    pub process_count: usize,
    pub processes: Vec<ProcessEntry>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ConnectionPollResult {
    pub connected: bool,
    pub attempts: u32,
    pub elapsed_time: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub server_info: Option<Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
}

pub async fn launch_game(
    options: &LaunchGameOptions,
    tcp_client: &TcpClient,
    settings: &Settings,
) -> Result<String, String> {
    let game_path = resolve_game_path(options.version, settings)?;

    if !game_path.exists() {
        return Err(format!("Error: Game path does not exist: {}", game_path.display()));
    }

    if let Some(detected) = detect_game_version(&game_path) {
        if detected != options.version {
            warn!(
                requested = options.version.as_display(),
                detected = detected.as_display(),
                path = %game_path.display(),
                "Requested game version differs from detected install layout"
            );
        }
    }

    if is_game_running(&settings.game_executable).await {
        return Err(
            "Error: Game is already running.\nPlease close the game first using s1_close_game, or kill it manually.".to_string(),
        );
    }

    let game_executable_path = game_path.join(settings.game_executable.trim());
    if !game_executable_path.exists() {
        return Err(format!(
            "Error: Game executable not found: {}",
            game_executable_path.display()
        ));
    }

    info!(
        version = options.version.as_display(),
        exe = %game_executable_path.display(),
        debugger = options.enable_debugger,
        "Launching game"
    );

    let ps_command = launch_command(&game_executable_path, options.enable_debugger);
    let output = run_command_with_timeout(
        "powershell",
        vec!["-NoProfile".to_string(), "-Command".to_string(), ps_command],
        Duration::from_secs(10),
    )
    .await?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        let text = stderr.trim();
        let detail = if text.is_empty() {
            "Unknown PowerShell error".to_string()
        } else {
            text.to_string()
        };
        return Err(format!("Error launching game:\n{detail}"));
    }

    info!("Waiting for game process to start");
    if !wait_for_game_launch(settings, Duration::from_secs(10)).await {
        return Ok(
            "Warning: Game process not detected after 10 seconds.\nThe game may be starting slowly or failed to launch."
                .to_string(),
        );
    }

    let mut response_text = format!(
        "Game launched successfully ({})\n  Path: {}\n",
        options.version.as_display(),
        game_path.display()
    );

    if options.enable_debugger {
        response_text.push_str("  Debugger: Enabled\n");
    }

    if options.wait_for_connection {
        let total_wait = settings.game_startup_timeout + 20;
        response_text.push_str(&format!(
            "\nWaiting for game to load and connect (up to {total_wait}s)...\n"
        ));
        response_text.push_str("  - Game needs ~20s to load Menu scene before accepting commands\n");

        let connection_result = poll_connection(
            tcp_client,
            Duration::from_secs(settings.game_startup_timeout),
            Duration::from_secs(settings.game_connection_poll_interval.max(1)),
        )
        .await;

        if connection_result.connected {
            response_text.push_str(&format!(
                "Connected to game server after {}s ({} attempts)\n",
                connection_result.elapsed_time, connection_result.attempts
            ));

            if let Some(Value::Object(server_info)) = &connection_result.server_info {
                let server_name = server_info
                    .get("server_name")
                    .and_then(Value::as_str)
                    .unwrap_or("Unknown");
                let version = server_info
                    .get("version")
                    .and_then(Value::as_str)
                    .unwrap_or("Unknown");

                response_text.push_str(&format!("  Server: {server_name} v{version}\n"));
            }
        } else {
            response_text.push_str("Failed to connect to game server\n");
            response_text.push_str(&format!("  Attempts: {}\n", connection_result.attempts));
            response_text.push_str(&format!(
                "  Error: {}\n",
                connection_result
                    .error
                    .as_deref()
                    .unwrap_or("Unknown error")
            ));
            response_text.push_str("\nThe game may still be loading. You can:\n");
            response_text.push_str("  - Wait and try connecting manually\n");
            response_text.push_str("  - Check MelonLoader logs for errors\n");
            response_text.push_str("  - Verify the mod is installed correctly\n");
        }
    }

    Ok(response_text)
}

pub async fn close_game(tcp_client: &TcpClient, settings: &Settings) -> Result<String, String> {
    if !is_game_running(&settings.game_executable).await {
        return Ok("Game is not currently running.".to_string());
    }

    info!("Closing game process");
    tcp_client.disconnect();

    let output = run_command_with_timeout(
        "taskkill",
        vec!["/F".to_string(), "/IM".to_string(), settings.game_executable.clone()],
        Duration::from_secs(5),
    )
    .await?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        let detail = if stderr.trim().is_empty() {
            "Unknown taskkill error"
        } else {
            stderr.trim()
        };
        return Err(format!("Error closing game: {detail}"));
    }

    let max_wait = Duration::from_secs(3);
    let check_interval = Duration::from_millis(300);
    let start = Instant::now();

    while start.elapsed() < max_wait {
        if !is_game_running(&settings.game_executable).await {
            return Ok("Game closed successfully".to_string());
        }
        sleep(check_interval).await;
    }

    if !is_game_running(&settings.game_executable).await {
        Ok("Game closed successfully".to_string())
    } else {
        Ok("Warning: Game process may still be running".to_string())
    }
}

pub async fn get_game_process_info(settings: &Settings) -> GameProcessInfo {
    let process_name = process_name_for_powershell(&settings.game_executable);
    let ps_command = format!(
        "Get-Process -Name \"{}\" -ErrorAction SilentlyContinue | Select-Object Id, CPU, WorkingSet, StartTime | ConvertTo-Json",
        process_name
    );

    let output = match run_command_with_timeout(
        "powershell",
        vec!["-NoProfile".to_string(), "-Command".to_string(), ps_command],
        Duration::from_secs(10),
    )
    .await
    {
        Ok(output) => output,
        Err(error) => {
            return GameProcessInfo {
                running: false,
                process_count: 0,
                processes: Vec::new(),
                error: Some(error),
            }
        }
    };

    if !output.status.success() {
        return GameProcessInfo {
            running: false,
            process_count: 0,
            processes: Vec::new(),
            error: Some(String::from_utf8_lossy(&output.stderr).trim().to_string()),
        };
    }

    let stdout = String::from_utf8_lossy(&output.stdout);
    let payload = stdout.trim();
    if payload.is_empty() {
        return GameProcessInfo {
            running: false,
            process_count: 0,
            processes: Vec::new(),
            error: None,
        };
    }

    let json_value = match serde_json::from_str::<Value>(payload) {
        Ok(value) => value,
        Err(error) => {
            return GameProcessInfo {
                running: false,
                process_count: 0,
                processes: Vec::new(),
                error: Some(format!("Failed to parse process info JSON: {error}")),
            }
        }
    };

    let entries = match json_value {
        Value::Array(items) => items,
        single => vec![single],
    };

    let processes: Vec<ProcessEntry> = entries
        .into_iter()
        .filter_map(|entry| {
            let object = entry.as_object()?;
            let pid = object.get("Id")?.as_i64()?;
            let cpu_time = object.get("CPU").and_then(Value::as_f64).unwrap_or(0.0);
            let working_set = object
                .get("WorkingSet")
                .and_then(Value::as_f64)
                .unwrap_or(0.0);
            let memory_mb = ((working_set / 1024.0 / 1024.0) * 100.0).round() / 100.0;
            let start_time = object
                .get("StartTime")
                .and_then(Value::as_str)
                .unwrap_or("")
                .to_string();

            Some(ProcessEntry {
                pid,
                cpu_time,
                memory_mb,
                start_time,
            })
        })
        .collect();

    GameProcessInfo {
        running: !processes.is_empty(),
        process_count: processes.len(),
        processes,
        error: None,
    }
}

pub async fn poll_connection(
    tcp_client: &TcpClient,
    timeout: Duration,
    interval: Duration,
) -> ConnectionPollResult {
    let start = Instant::now();
    let mut attempt = 0_u32;

    debug!("Disconnecting any existing connection before polling");
    tcp_client.disconnect();
    sleep(Duration::from_millis(500)).await;

    let initial_delay = Duration::from_secs(20);
    info!(
        delay_secs = initial_delay.as_secs(),
        "Waiting for game to fully load before first connection attempt"
    );
    sleep(initial_delay).await;

    while start.elapsed() < timeout {
        attempt += 1;

        if tcp_client.is_connected() {
            debug!(attempt, "TCP client reported connected; forcing disconnect for clean retry");
            tcp_client.disconnect();
            sleep(Duration::from_millis(200)).await;
        }

        debug!(attempt, "Attempting handshake connection");
        match tcp_client.async_call("handshake", Some(json!({}))).await {
            Ok(response) => {
                if let Some(error_response) = response.error {
                    debug!(
                        attempt,
                        code = error_response.code,
                        message = %error_response.message,
                        "Handshake returned error"
                    );
                    tcp_client.disconnect();
                } else {
                    return ConnectionPollResult {
                        connected: true,
                        attempts: attempt,
                        elapsed_time: round_to_2(start.elapsed().as_secs_f64()),
                        server_info: response.result,
                        error: None,
                    };
                }
            }
            Err(error) => {
                debug!(attempt, "Connection attempt failed: {error}");
                tcp_client.disconnect();
            }
        }

        if start.elapsed() < timeout {
            sleep(interval).await;
        }
    }

    ConnectionPollResult {
        connected: false,
        attempts: attempt,
        elapsed_time: round_to_2(start.elapsed().as_secs_f64()),
        server_info: None,
        error: Some(format!(
            "Connection timeout after {} seconds ({} attempts)",
            timeout.as_secs(),
            attempt
        )),
    }
}

fn resolve_game_path(version: GameVersion, settings: &Settings) -> Result<PathBuf, String> {
    let raw_path = match version {
        GameVersion::Il2cpp => settings.game_il2cpp_path.trim(),
        GameVersion::Mono => settings.game_mono_path.trim(),
    };

    if raw_path.is_empty() {
        return Err(format!(
            "Error: No game path configured for {} version.\nPlease set 'game_{}_path' in your config.json file.\nExample: game_config.json.example",
            version.as_display(),
            match version {
                GameVersion::Il2cpp => "il2cpp",
                GameVersion::Mono => "mono",
            }
        ));
    }

    if settings.game_executable.trim().is_empty() {
        return Err(
            "Error: game_executable is not configured. Please set it in S1MCPClientRust/config.json."
                .to_string(),
        );
    }

    Ok(PathBuf::from(raw_path))
}

fn detect_game_version(game_path: &Path) -> Option<GameVersion> {
    if !game_path.exists() {
        return None;
    }

    let il2cpp_marker = game_path.join("MelonLoader").join("Il2CppAssemblies");
    if il2cpp_marker.is_dir() {
        Some(GameVersion::Il2cpp)
    } else {
        Some(GameVersion::Mono)
    }
}

async fn is_game_running(game_executable: &str) -> bool {
    let executable = if game_executable.trim().is_empty() {
        "Schedule I.exe"
    } else {
        game_executable.trim()
    };

    let filter = format!("IMAGENAME eq {executable}");
    let output = run_command_with_timeout(
        "tasklist",
        vec!["/FI".to_string(), filter],
        Duration::from_secs(5),
    )
    .await;

    match output {
        Ok(output) => {
            let stdout = String::from_utf8_lossy(&output.stdout);
            stdout.contains(executable)
        }
        Err(error) => {
            debug!("Error checking game process state: {error}");
            false
        }
    }
}

async fn wait_for_game_launch(settings: &Settings, timeout: Duration) -> bool {
    let start = Instant::now();
    while start.elapsed() < timeout {
        if is_game_running(&settings.game_executable).await {
            info!("Game process detected");
            return true;
        }
        sleep(Duration::from_millis(500)).await;
    }

    warn!(timeout_secs = timeout.as_secs_f64(), "Game process not detected before timeout");
    false
}

async fn run_command_with_timeout(
    program: &str,
    args: Vec<String>,
    timeout: Duration,
) -> Result<Output, String> {
    let program_owned = program.to_string();
    let join_handle = tokio::task::spawn_blocking(move || Command::new(program_owned).args(args).output());

    match tokio::time::timeout(timeout, join_handle).await {
        Ok(join_result) => match join_result {
            Ok(output_result) => {
                output_result.map_err(|error| format!("Error running command: {error}"))
            }
            Err(error) => Err(format!("Error waiting for command task: {error}")),
        },
        Err(_) => Err(format!("Error: Command timed out after {} seconds", timeout.as_secs())),
    }
}

fn launch_command(game_executable_path: &Path, enable_debugger: bool) -> String {
    let exe = escape_for_single_quoted_powershell(&game_executable_path.to_string_lossy());
    if enable_debugger {
        format!(
            "Start-Process -FilePath '{exe}' -ArgumentList '--melonloader.launchdebugger','--melonloader.debug'"
        )
    } else {
        format!("Start-Process -FilePath '{exe}'")
    }
}

fn process_name_for_powershell(game_executable: &str) -> String {
    let trimmed = game_executable.trim();
    let without_exe = trimmed.strip_suffix(".exe").unwrap_or(trimmed);
    without_exe.to_string()
}

fn escape_for_single_quoted_powershell(input: &str) -> String {
    input.replace('\'', "''")
}

fn round_to_2(value: f64) -> f64 {
    (value * 100.0).round() / 100.0
}

// TODO: parity-followup - preserve global connection/instruction state updates from Python main module.
