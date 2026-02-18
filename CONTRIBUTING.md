# Contributing to CMF Planner

Thank you for your interest in contributing to CMF Planner! This project aims to create an open-source virtual surgical planning tool for orthognathic surgery.

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/YOUR_USERNAME/CMFPlanner_C-.git`
3. Create a feature branch: `git checkout -b feature/your-feature-name`
4. Make your changes
5. Build and test: `dotnet build && dotnet test`
6. Commit with descriptive message: `git commit -m "E{epic}-T{task}: description"`
7. Push to your fork: `git push origin feature/your-feature-name`
8. Open a Pull Request

## Development Requirements

- Windows 10/11
- .NET 8 SDK
- Visual Studio 2022 (recommended)

## Code Standards

- Use C# conventions (PascalCase for public members, camelCase for private)
- Enable nullable reference types
- Write XML documentation for public APIs
- Keep methods focused and under 50 lines where possible

## Commit Message Format

```
E{epic}-T{task}: {short description}
```

Example: `E2-T1: Implement DICOM folder loader`

## Plugin Development

Plugins implement interfaces defined in `CMFPlanner.Plugins`:
- `IVisualizationPlugin`
- `ISegmentationPlugin`
- `IPlanningPlugin`
- `IExportPlugin`

Place compiled plugin assemblies in the `/plugins` directory.

## Reporting Issues

- Use GitHub Issues
- Include steps to reproduce
- Attach relevant DICOM/STL test data (anonymized only!)

## Code of Conduct

Be respectful, constructive, and inclusive. This is a medical software project — accuracy and safety matter.
