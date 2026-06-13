# NovelEngine.Runtime

Native C++20 runtime layer. It currently implements the shared scripting state
and condition evaluator through a stable C ABI.

Build requirements:

- CMake 3.20+;
- MSVC, Clang, or GCC with C++20 support.

```powershell
cmake -S . -B build
cmake --build build --config Release
```

The editor uses the managed C# implementation for instant validation. The
standalone player will load the native library and execute the same language.
