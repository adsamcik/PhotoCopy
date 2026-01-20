# Contributing to PhotoCopy

Thank you for your interest in contributing to PhotoCopy! We welcome contributions from the community.

## Prerequisites

* [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Getting Started

1. **Fork the repository** on GitHub.
2. **Clone your fork** locally:

    ```bash
    git clone https://github.com/your-username/PhotoCopy.git
    cd PhotoCopy
    ```

3. **Restore dependencies**:

    ```bash
    dotnet restore
    ```

## Building the Project

To build the project, run the following command in the root directory:

```bash
dotnet build
```

## Running Tests

This project uses xUnit and AwesomeAssertions. To run the tests:

```bash
dotnet test
```

## Coding Guidelines

* Follow standard C# coding conventions.
* Ensure all new code is covered by unit tests.
* Keep code clean and readable.

## Documentation

PhotoCopy has documentation in multiple places:

* **README.md** - User-facing documentation with quick start and examples
* **docs/destination-variables.md** - Complete reference for destination path variables and fallback syntax
* **docs/design/** - Technical design documents for complex features:
  * `conditional-variables.md` - Design spec for threshold-based variables
  * `two-pass-architecture.md` - Statistics collection architecture

When adding new features:

* Update README.md with user-facing documentation
* Add or update relevant docs/ files for detailed reference
* For complex features, add a design document in docs/design/

## Submitting a Pull Request

1. Create a new branch for your feature or bug fix:

    ```bash
    git checkout -b feature/your-feature-name
    ```

2. Commit your changes with clear and descriptive messages.
3. Push your branch to your fork:

    ```bash
    git push origin feature/your-feature-name
    ```

4. Open a Pull Request against the `main` branch of the original repository.
5. Provide a clear description of your changes and reference any related issues.

## Reporting Issues

If you find a bug or have a feature request, please open an issue on GitHub. Provide as much detail as possible, including steps to reproduce the issue.

## License

By contributing, you agree that your contributions will be licensed under its MIT License.
