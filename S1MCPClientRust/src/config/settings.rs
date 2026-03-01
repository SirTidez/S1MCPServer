use std::fs;
use std::path::Path;

use serde::{Deserialize, Serialize};

const CONFIG_PATHS: [&str; 2] = ["S1MCPClientRust/config.json", "config.json"];
const SETTINGS_KEYS: [&str; 10] = [
    "host",
    "port",
    "log_level",
    "connection_timeout",
    "reconnect_delay",
    "game_il2cpp_path",
    "game_mono_path",
    "game_executable",
    "game_startup_timeout",
    "game_connection_poll_interval",
];

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ConfigDiagnosticKind {
    MissingConfigFile,
    ConfigReadError,
    InvalidConfig,
    PartialFallback,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ConfigDiagnostic {
    pub kind: ConfigDiagnosticKind,
    pub message: String,
}

#[derive(Debug, Clone)]
pub struct SettingsLoadReport {
    pub settings: Settings,
    pub diagnostics: Vec<ConfigDiagnostic>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(default)]
pub struct Settings {
    pub host: String,
    pub port: u16,
    pub log_level: String,
    pub connection_timeout: u64,
    pub reconnect_delay: u64,
    pub game_il2cpp_path: String,
    pub game_mono_path: String,
    pub game_executable: String,
    pub game_startup_timeout: u64,
    pub game_connection_poll_interval: u64,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            host: "127.0.0.1".to_string(),
            port: 8765,
            log_level: "info".to_string(),
            connection_timeout: 10,
            reconnect_delay: 3,
            game_il2cpp_path: String::new(),
            game_mono_path: String::new(),
            game_executable: String::new(),
            game_startup_timeout: 120,
            game_connection_poll_interval: 2,
        }
    }
}

impl Settings {
    pub fn load() -> Self {
        Self::load_with_diagnostics().settings
    }

    pub fn load_with_diagnostics() -> SettingsLoadReport {
        if let Ok(path) = std::env::var("S1MCPCLIENTRUST_CONFIG") {
            let path = Path::new(&path);
            return Self::load_from_path_with_diagnostics(path);
        }

        for config_path in CONFIG_PATHS {
            let path = Path::new(config_path);
            if path.exists() {
                return Self::load_from_path_with_diagnostics(path);
            }
        }

        SettingsLoadReport {
            settings: Self::default(),
            diagnostics: vec![ConfigDiagnostic {
                kind: ConfigDiagnosticKind::MissingConfigFile,
                message: format!(
                    "Config file was not found. Checked paths: {}. Using default settings.",
                    CONFIG_PATHS.join(", ")
                ),
            }],
        }
    }

    fn load_from_path_with_diagnostics(config_path: &Path) -> SettingsLoadReport {
        let mut diagnostics = Vec::new();

        let contents = match fs::read_to_string(config_path) {
            Ok(value) => value,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                diagnostics.push(ConfigDiagnostic {
                    kind: ConfigDiagnosticKind::MissingConfigFile,
                    message: format!(
                        "Config file '{}' was not found. Using default settings.",
                        config_path.display()
                    ),
                });
                return SettingsLoadReport {
                    settings: Self::default(),
                    diagnostics,
                };
            }
            Err(error) => {
                diagnostics.push(ConfigDiagnostic {
                    kind: ConfigDiagnosticKind::ConfigReadError,
                    message: format!(
                        "Failed to read config file '{}': {}. Using default settings.",
                        config_path.display(),
                        error
                    ),
                });
                return SettingsLoadReport {
                    settings: Self::default(),
                    diagnostics,
                };
            }
        };

        let parsed_value = match serde_json::from_str::<serde_json::Value>(&contents) {
            Ok(value) => value,
            Err(error) => {
                diagnostics.push(ConfigDiagnostic {
                    kind: ConfigDiagnosticKind::InvalidConfig,
                    message: format!(
                        "Config file '{}' is invalid JSON: {}. Using default settings.",
                        config_path.display(),
                        error
                    ),
                });
                return SettingsLoadReport {
                    settings: Self::default(),
                    diagnostics,
                };
            }
        };

        let Some(config_obj) = parsed_value.as_object() else {
            diagnostics.push(ConfigDiagnostic {
                kind: ConfigDiagnosticKind::InvalidConfig,
                message: format!(
                    "Config file '{}' must contain a JSON object. Using default settings.",
                    config_path.display()
                ),
            });
            return SettingsLoadReport {
                settings: Self::default(),
                diagnostics,
            };
        };

        let missing_keys: Vec<&str> = SETTINGS_KEYS
            .iter()
            .copied()
            .filter(|key| !config_obj.contains_key(*key))
            .collect();

        if !missing_keys.is_empty() {
            diagnostics.push(ConfigDiagnostic {
                kind: ConfigDiagnosticKind::PartialFallback,
                message: format!(
                    "Config file '{}' is missing keys [{}]. Defaults were applied for missing values.",
                    config_path.display(),
                    missing_keys.join(", ")
                ),
            });
        }

        match serde_json::from_value::<Self>(parsed_value) {
            Ok(settings) => SettingsLoadReport {
                settings,
                diagnostics,
            },
            Err(error) => {
                diagnostics.push(ConfigDiagnostic {
                    kind: ConfigDiagnosticKind::InvalidConfig,
                    message: format!(
                        "Config file '{}' has invalid values: {}. Using default settings.",
                        config_path.display(),
                        error
                    ),
                });
                SettingsLoadReport {
                    settings: Self::default(),
                    diagnostics,
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::{ConfigDiagnosticKind, Settings};
    use std::fs;
    use std::path::PathBuf;
    use std::time::{SystemTime, UNIX_EPOCH};

    fn temp_file_path(name: &str) -> PathBuf {
        let mut path = std::env::temp_dir();
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system clock should be valid")
            .as_nanos();
        path.push(format!("s1mcprust-{name}-{nanos}.json"));
        path
    }

    #[test]
    fn load_with_diagnostics_reports_missing_config() {
        let path = temp_file_path("missing");
        let report = Settings::load_from_path_with_diagnostics(&path);

        assert_eq!(report.settings.host, Settings::default().host);
        assert!(report
            .diagnostics
            .iter()
            .any(|diag| diag.kind == ConfigDiagnosticKind::MissingConfigFile));
    }

    #[test]
    fn load_with_diagnostics_reports_partial_fallback() {
        let path = temp_file_path("partial");
        fs::write(&path, r#"{"host":"10.0.0.5"}"#).expect("config write should succeed");

        let report = Settings::load_from_path_with_diagnostics(&path);
        let _ = fs::remove_file(&path);

        assert_eq!(report.settings.host, "10.0.0.5");
        assert!(report
            .diagnostics
            .iter()
            .any(|diag| diag.kind == ConfigDiagnosticKind::PartialFallback));
    }

    #[test]
    fn load_with_diagnostics_reports_invalid_json() {
        let path = temp_file_path("invalid");
        fs::write(&path, "{not-valid-json").expect("config write should succeed");

        let report = Settings::load_from_path_with_diagnostics(&path);
        let _ = fs::remove_file(&path);

        assert_eq!(report.settings.port, Settings::default().port);
        assert!(report
            .diagnostics
            .iter()
            .any(|diag| diag.kind == ConfigDiagnosticKind::InvalidConfig));
    }
}
