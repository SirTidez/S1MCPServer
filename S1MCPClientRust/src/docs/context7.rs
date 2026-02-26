use std::time::Duration;

use reqwest::{Client, StatusCode, Url};
use tokio::time::sleep;
use tracing::{debug, error, warn};

const CONTEXT7_BASE_URL: &str = "https://context7.com/ifbars/s1api/llms.txt";
const REQUEST_TIMEOUT_SECONDS: u64 = 30;
const MAX_RETRY_ATTEMPTS: u32 = 3;
const BASE_BACKOFF_MS: u64 = 300;
const MAX_BACKOFF_MS: u64 = 2_000;

pub async fn search_s1api_docs(topic: &str, tokens: u32) -> Result<String, String> {
    let topic = topic.trim();
    if topic.is_empty() {
        return Err("Error: topic parameter is required".to_string());
    }

    if tokens == 0 {
        return Err("Error: tokens must be a positive integer".to_string());
    }

    let url = Url::parse_with_params(CONTEXT7_BASE_URL, &[("topic", topic), ("tokens", &tokens.to_string())])
        .map_err(|error| format!("Error: Failed to build docs search URL - {error}"))?;

    debug!(topic = %topic, tokens, url = %url, "Searching S1API docs via Context7");

    let client = Client::builder()
        .timeout(Duration::from_secs(REQUEST_TIMEOUT_SECONDS))
        .build()
        .map_err(|error| format!("Error: Failed to initialize HTTP client - {error}"))?;

    let mut last_network_error: Option<String> = None;
    for attempt in 1..=MAX_RETRY_ATTEMPTS {
        let response = match client.get(url.clone()).send().await {
            Ok(response) => response,
            Err(error) => {
                if is_retryable_request_error(&error) && attempt < MAX_RETRY_ATTEMPTS {
                    let backoff = retry_backoff(attempt);
                    warn!(
                        topic = %topic,
                        attempt,
                        max_attempts = MAX_RETRY_ATTEMPTS,
                        backoff_ms = backoff.as_millis(),
                        "Transient Context7 request failure: {error}"
                    );
                    last_network_error = Some(error.to_string());
                    sleep(backoff).await;
                    continue;
                }

                if error.is_timeout() {
                    error!(topic = %topic, attempts = attempt, "Context7 request timed out: {error}");
                    return Err(format!(
                        "Error: Request timed out while fetching documentation for topic '{topic}' after {attempt} attempt(s). Please retry shortly."
                    ));
                }

                error!(topic = %topic, attempts = attempt, "Context7 request failed: {error}");
                return Err(format!(
                    "Error: Network error while fetching documentation after {attempt} attempt(s): {error}. Please check your internet connection and retry."
                ));
            }
        };

        if should_retry_status(response.status()) && attempt < MAX_RETRY_ATTEMPTS {
            let status = response.status().as_u16();
            let backoff = retry_backoff(attempt);
            warn!(
                topic = %topic,
                attempt,
                max_attempts = MAX_RETRY_ATTEMPTS,
                status,
                backoff_ms = backoff.as_millis(),
                "Context7 returned transient HTTP status; retrying"
            );
            sleep(backoff).await;
            continue;
        }

        if !response.status().is_success() {
            let status = response.status().as_u16();
            error!(topic = %topic, status, attempts = attempt, "Context7 returned HTTP error");
            return Err(format!(
                "Error: HTTP {status} while fetching documentation for topic '{topic}'. Please verify the query and try again later."
            ));
        }

        let content = response
            .text()
            .await
            .map_err(|error| format!("Error: Failed to read documentation response - {error}"))?;

        debug!(topic = %topic, content_len = content.len(), "Retrieved Context7 documentation content");

        if content.trim().is_empty() {
            return Ok(format!(
                "No documentation found for topic: '{topic}'. Try a different search term."
            ));
        }

        return Ok(content);
    }

    let fallback_error = last_network_error.unwrap_or_else(|| "unknown transient failure".to_string());
    Err(format!(
        "Error: Unable to fetch documentation for topic '{topic}' after {MAX_RETRY_ATTEMPTS} attempts due to transient failures: {fallback_error}."
    ))
}

fn is_retryable_request_error(error: &reqwest::Error) -> bool {
    error.is_timeout() || error.is_connect() || error.is_request()
}

fn should_retry_status(status: StatusCode) -> bool {
    status.is_server_error()
}

fn retry_backoff(attempt: u32) -> Duration {
    let exponent = attempt.saturating_sub(1).min(5);
    let factor = 1_u64 << exponent;
    let delay_ms = BASE_BACKOFF_MS.saturating_mul(factor).min(MAX_BACKOFF_MS);
    Duration::from_millis(delay_ms)
}

#[cfg(test)]
mod tests {
    use super::{retry_backoff, should_retry_status};
    use reqwest::StatusCode;

    #[test]
    fn retry_backoff_grows_and_caps() {
        let first = retry_backoff(1);
        let second = retry_backoff(2);
        let third = retry_backoff(3);
        let high = retry_backoff(9);

        assert!(second > first);
        assert!(third > second);
        assert_eq!(high.as_millis(), 2_000);
    }

    #[test]
    fn only_server_errors_are_retryable_statuses() {
        assert!(should_retry_status(StatusCode::INTERNAL_SERVER_ERROR));
        assert!(should_retry_status(StatusCode::BAD_GATEWAY));
        assert!(!should_retry_status(StatusCode::TOO_MANY_REQUESTS));
        assert!(!should_retry_status(StatusCode::NOT_FOUND));
    }
}
