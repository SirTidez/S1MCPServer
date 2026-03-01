use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Acknowledgment {
    pub id: u64,
    #[serde(default = "default_status")]
    pub status: String,
}

fn default_status() -> String {
    "received".to_string()
}
