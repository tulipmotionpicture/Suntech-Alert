# Suntech Alert - SQL Server Wait Stats Monitor

A desktop application for real-time monitoring of SQL Server wait statistics through Redis/KeyDB pub/sub channel.

## Overview

Suntech Alert is a .NET Windows Forms application designed to monitor SQL Server performance metrics, specifically focused on wait statistics. The application connects to a Redis or KeyDB instance, subscribes to a specified channel, and displays incoming metrics in a real-time dashboard with configurable thresholds for warning and alert conditions.

## Features

- **Redis/KeyDB Integration**: Connect to any Redis or KeyDB server by specifying host and port.
- **Flexible Channel Subscription**: Monitor data from any Redis pub/sub channel.
- **Real-Time Metrics Display**: View incoming SQL Server wait statistics in a data grid.
- **Automatic Status Classification**: Metrics are automatically classified as Normal, Warning, or Alert based on configurable thresholds.
- **Visual Indicators**: Color-coded display of metrics (yellow for warnings, red for alerts).
- **Adjustable Thresholds**: Configure warning and alert thresholds through an interactive UI.
- **Persistent Settings**: Threshold settings are saved between application sessions.
- **Event Logging**: Comprehensive event log for monitoring connections and data receipt.

## Requirements

- .NET Framework 4.0 or higher.
- Windows 7 or later operating system.
- Redis or KeyDB server (local or remote).
- SQL Server instance publishing wait statistics to Redis.

## Dependencies

- **StackExchange.Redis**: For Redis connectivity.
- **Newtonsoft.Json**: For JSON parsing.

## Installation

1. Clone the repository:git clone https://github.com/yourusername/suntech-alert.git2. Open the `Suntech Alert.sln` solution file in Visual Studio.
3. Restore NuGet packages:nuget restore "Suntech Alert.sln"4. Build the solution:msbuild "Suntech Alert.sln" /p:Configuration=Release5. Run the application by executing the generated `Suntech Alert.exe` file.

## Usage

### Connection Setup

1. Enter the Redis/KeyDB server address in the format `hostname:port` (default: `localhost:6379`).
2. Specify the channel name to subscribe to (default: `sqlserver:waitstats`).
3. Click "Connect" to establish the connection.

### Monitoring Metrics

- The main grid displays all received metrics with their values and status.
- Wait time metrics are color-coded based on their values:
  - **White**: Normal (below warning threshold).
  - **Yellow**: Warning (between warning and alert thresholds).
  - **Red**: Alert (above alert threshold).

### Adjusting Thresholds

1. Use the sliders in the "Wait Time Thresholds" section at the bottom of the window.
2. Set appropriate thresholds for Warning and Alert levels.
3. Changes are applied immediately and affect the display of current metrics.
4. Click "Reset Defaults" to restore default threshold values (Warning: 10,000ms, Alert: 40,000ms).

## Data Format

The application expects JSON data on the subscribed Redis channel in the following format:
[
  {
    "metric_type": "WaitStats",
    "sub_metric": "ASYNC_NETWORK_IO",
    "value": "12345",
    "extra_info": "tasks=42, avg_wait=294.4",
    "capture_time": "2023-04-15T14:30:00"
  }
]
## Development

### Building from Source

1. Clone the repository:git clone https://github.com/tulipmotionpicture/suntech-alert.git. Open the `Suntech Alert.sln` solution file in Visual Studio.
3. Restore NuGet packages:nuget restore "Suntech Alert.sln"4. Build the solution:msbuild "Suntech Alert.sln" /p:Configuration=Release
## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Acknowledgments

- This application was developed by Suntech to help database administrators monitor SQL Server performance.
