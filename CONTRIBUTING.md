# Contributing Guidelines

Contributions are welcome. By contributing, you agree that your contribution is licensed under the same MIT license as this project and that you will follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Contribution Process

Open an [issue](https://github.com/spotflow-io/base62-dotnet/issues/new) before starting a significant feature or behavioral change so the approach can be discussed first. Small fixes may be submitted directly.

Pull requests should:

- Address one logical change.
- Include tests for new or changed behavior.
- Preserve the existing encoded format unless a breaking change has been explicitly agreed upon.
- Update the documentation when public behavior changes.
- Pass `dotnet format --verify-no-changes`, `dotnet build`, and `dotnet test`.

## Development

Install a .NET 10 SDK, then run:

```bash
dotnet restore
dotnet build
dotnet test
```

The library targets .NET 8, .NET 9, and .NET 10. Changes must build and pass tests on every target framework.

## Compatibility

The alphabet, byte order, digit order, block widths, and padding are part of the serialized format. Do not change them without treating the change as a new, incompatible format. Avoid breaking changes to the public API; discuss any unavoidable break in an issue first.
