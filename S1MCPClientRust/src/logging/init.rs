use tracing_subscriber::fmt;
use tracing_subscriber::EnvFilter;

pub fn init_logger(level: &str) {
    let env_filter = EnvFilter::try_new(level).unwrap_or_else(|_| EnvFilter::new("info"));

    fmt()
        .with_env_filter(env_filter)
        .with_writer(std::io::stderr)
        .with_target(false)
        .compact()
        .init();
}
