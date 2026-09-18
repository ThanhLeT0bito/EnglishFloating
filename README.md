# EnglishFloating

A lightweight Windows desktop floating widget application designed for practicing English and PTE (Pearson Test of English) sentences, vocabulary cards, and interactive review practice modes.

## Features

- **Floating Overlay Widget**: Always-on-top, draggable, minimalist floating bar displaying target sentences and vocabulary cards.
- **Review Practice Mode**:
  - Masked input / fill-in-the-blank practice directly on the floating widget or in Settings Center.
  - Automatic focus advancement (typing or Space key).
  - Immediate visual feedback on answer correctness and progress tracking.
- **Vocabulary & Explanations**: Rich card popups with IPA, word definitions, and Vietnamese translations.
- **Settings Center**: Comprehensive management for study lists, display modes, font sizes, opacity, and practice options.

## Tech Stack

- **Framework**: .NET 9.0 / WPF (Windows Presentation Foundation)
- **Testing**: xUnit, FluentAssertions, Moq
- **Design System**: Modern dark theme with customizable transparency and borderless styling

## Getting Started

### Prerequisites

- Windows 10/11
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

### Build & Run

```powershell
# Restore dependencies and build
dotnet build

# Run unit tests
dotnet test

# Launch the application
dotnet run --project src/PteFloatingSentence.Windows
```
