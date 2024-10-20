

# DubSense

DubSense is a Windows application that monitors a specific portion of the screen for the text "CTO" using Optical Character Recognition (OCR) and sends a webhook notification when the text is detected. The application minimizes to the system tray and can automatically start and stop monitoring based on the presence of a specific process.

## Features

- **OCR Monitoring**: Uses Tesseract OCR to detect the text "CTO" in a specified screen area.
- **Webhook Notifications**: Sends a webhook notification when the text is detected.
- **Auto Monitoring**: Automatically starts and stops monitoring based on the presence of a specific process.
- **System Tray Integration**: Minimizes to the system tray and provides context menu options for restoring or exiting the application.
- **Settings Persistence**: Saves and loads the webhook URL from application settings.

## Requirements

- .NET 8.0
- Tesseract OCR data files (`tessdata` folder with `eng.traineddata`)

## Installation

1. **Clone the repository**:

2. **Build the project**:
    - Open the solution in Visual Studio.
    - Build the project to generate the executable.

## Usage

1. **Run the application**:
    - Launch the executable generated from the build process.

2. **Configure Webhook URL**:
    - Enter a valid webhook URL in the provided text box.

3. **Start Monitoring**:
    - Click the "Start" button to begin monitoring the screen for the text "CTO".

4. **Auto Monitoring**:
    - Check the "Auto Monitor" checkbox to enable automatic monitoring based on the presence of the "cod" process.

5. **Minimize to Tray**:
    - Minimize the application to the system tray. Right-click the tray icon for options to restore or exit the application.

## Development

### Prerequisites

- Visual Studio 2022 or later
- .NET 8.0 SDK

### Building the Project

1. **Open the solution**:
    - Open `DubSense.sln` in Visual Studio.

2. **Restore NuGet packages**:
    - Visual Studio will automatically restore the required NuGet packages.

3. **Build the solution**:
    - Build the solution using Visual Studio.

### Running the Application

- Press `F5` in Visual Studio to run the application in debug mode.

## Contributing

Contributions are welcome! Please fork the repository and submit pull requests.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

## Acknowledgements

- [Tesseract OCR](https://github.com/tesseract-ocr/tesseract) for the OCR engine.
- [NotifyIcon](https://docs.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon) for system tray integration.