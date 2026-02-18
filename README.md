# CMF Planner v1.0

**Virtual Surgical Planning (VSP) for Orthognathic Surgery**

CMF Planner is an open-source desktop application for planning orthognathic surgical procedures including LeFort I, BSSO, Genioplasty, and custom multipart osteotomies.

## Features

- DICOM import (CT and CBCT)
- Hard and soft tissue segmentation
- Anatomical segment identification (skull, mandible, teeth)
- Dental cast superimposition (STL to CT registration via ICP)
- Natural Head Position (NHP) orientation
- Osteotomy planning with interactive cut planes
- 3D bone segment movement with real-time soft tissue simulation
- Surgical splint generation (intermediate + final)
- STL export and PDF surgical report

## Tech Stack

| Component | Technology |
|-----------|------------|
| Language | C# .NET 8 |
| UI Framework | WPF (Windows Presentation Foundation) |
| Plugin System | MEF (Managed Extensibility Framework) |
| 3D Visualization | VTK.NET, Helix Toolkit |
| DICOM Processing | fo-dicom |
| Math | MathNet.Numerics |
| Theme | MaterialDesignThemes (Dark) |

## Project Structure

```
CMFPlanner/
├── CMFPlanner.sln
├── src/
│   ├── CMFPlanner.Core/          # Core models, DI, shared utilities
│   ├── CMFPlanner.UI/            # WPF main application
│   ├── CMFPlanner.Dicom/         # DICOM import and volume reconstruction
│   ├── CMFPlanner.Segmentation/  # Tissue segmentation (threshold, marching cubes)
│   ├── CMFPlanner.Visualization/ # 3D rendering (VTK, Helix Toolkit)
│   ├── CMFPlanner.Planning/      # Osteotomy planning and segment movement
│   ├── CMFPlanner.Splint/        # Surgical splint generation
│   └── CMFPlanner.Plugins/       # MEF plugin interfaces and loader
├── plugins/                      # Runtime plugin directory
├── tests/                        # Unit and integration tests
├── docs/                         # Documentation
└── assets/                       # Icons, sample data, resources
```

## Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) (recommended)

## Build

```bash
dotnet build
```

## Run

```bash
dotnet run --project src/CMFPlanner.UI
```

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Disclaimer

CMF Planner is a research and educational tool. It is **not** a certified medical device (MDR/FDA). Do not use for clinical decision-making without appropriate regulatory clearance.
