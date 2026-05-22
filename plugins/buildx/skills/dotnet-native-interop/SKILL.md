---
name: dotnet-native-interop
description: Native interop reference for .NET 10 / C# 14. Covers P/Invoke (legacy `[DllImport]` + source-generated `[LibraryImport]`), default and custom marshalling, struct layout, calling conventions, `unsafe` + `delegate*` function pointers, `[UnmanagedCallersOnly]` reverse P/Invoke, NativeAOT exports + direct P/Invoke, COM (built-in + source-gen `[GeneratedComInterface]`), `NativeLibrary` + `DllImportResolver`, `SafeHandle`/`GCHandle`/pinning, allocator pairing, exception interop, cross-platform pitfalls.
when_to_use: |
  - Trigger keywords: P/Invoke, DllImport, LibraryImport, MarshalAs, StringMarshalling, StructLayout, fixed buffer, UnmanagedCallConv, CallConvCdecl, SuppressGCTransition, unsafe, delegate*, UnmanagedCallersOnly, NativeAOT, DirectPInvoke, ComImport, GeneratedComInterface, ComWrappers, NativeLibrary, SafeHandle, GCHandle, NativeMemory, SYSLIB1054, DisableRuntimeMarshalling.
  - Task shapes: write a P/Invoke; migrate `[DllImport]` → `[LibraryImport]`; design a struct matching a native layout; marshal a callback; export a managed entry point to C (NativeAOT); wrap a COM interface; debug `EntryPointNotFoundException` / `AccessViolationException` / heap corruption on free; port a P/Invoke from Windows to Linux/macOS.
allowed-tools: Bash, Edit, Glob, Grep, Monitor, NotebookEdit, PowerShell, Read, Write
user-invocable: false
paths: ["**/*.cs", "**/*.csproj"]
---

# .NET Native Interop — Reference

Reference for crossing the managed/native boundary on .NET 10 / C# 14. Pin the rules; defer the long marshalling tables to the Microsoft docs cited at the bottom.

## Mental model

- Two surfaces: **forward P/Invoke** (managed → native) and **reverse P/Invoke** (native → managed via `[UnmanagedCallersOnly]` or `Marshal.GetFunctionPointerForDelegate`).
- `[LibraryImport]` is **the** P/Invoke shape on .NET 7+. Marshalling glue is generated as C# at compile time → AOT-safe, debuggable, faster than the JIT-IL stub `[DllImport]` produces.
- `[GeneratedComInterface]` / `[GeneratedComClass]` are the COM equivalent — same source-gen, AOT-safe, the future-facing API. Built-in COM with `[ComImport]` still works on Windows but is runtime-IL and not AOT-compatible.
- "Blittable" = same bit layout managed/native ⇒ no copy, just pin. Non-blittable = marshaller allocates and copies. Knowing which is which controls allocations and correctness.
- The runtime marshaller is **on by default**. `[assembly: DisableRuntimeMarshalling]` switches the whole assembly to "blittable-only or it doesn't compile" — all-or-nothing.
- Native code runs **outside** the GC's view. Anything the native side keeps a pointer to must be pinned (`fixed` / `GCHandle.Pinned`) or rooted (`SafeHandle` / static field / `GC.KeepAlive`) for as long as the pointer is in use.

## Non-negotiable rules

1. **Use `[LibraryImport]` for new P/Invokes.** SYSLIB1054 flags `[DllImport]` candidates and ships a code-fixer; the migration cheat-sheet at the bottom covers every field. The containing type must be `partial`; the project must set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`.
2. **Always set `StringMarshalling` (or `CharSet` on legacy).** ANSI is a footgun. Pick `Utf8` (Unix-origin lib), `Utf16` (Win32 `*W` APIs), or write a custom marshaller — never ship default code-page behavior.
3. **`SetLastError = true` only when the native API documents `GetLastError`.** Read the value with `Marshal.GetLastPInvokeError()` (preferred); `Marshal.GetLastWin32Error()` is now an alias.
4. **`[UnmanagedCallersOnly]` callbacks must be `static` with all-blittable signatures, and exceptions thrown out of them terminate the process.** Catch at the boundary and convert to an HRESULT / error code.
5. **Function pointers obtained via `Marshal.GetFunctionPointerForDelegate` do NOT root the delegate.** Root it (static field or `GC.KeepAlive`) for the entire lifetime of the native pointer or the next GC will free a delegate that native code is still calling.
6. **Allocator pairing is mandatory.** Free a buffer with the same allocator that produced it. Never mix `malloc`/`free` with `CoTaskMem*` or `AllocHGlobal/FreeHGlobal`. When a foreign DLL gave you the buffer, call its own free.
7. **Use `SafeHandle` for OS handles.** It's the only mechanism that survives async aborts and prevents handle-recycling attacks. `IntPtr` + finalizer is wrong.
8. **Cross-platform: never use `wchar_t*` in shared APIs.** It is 2 bytes on Windows and (typically) 4 bytes on Linux/macOS. Use `char*` with an explicit UTF-8 contract, or use the `byte*` / `Utf8StringMarshaller` path.
9. **Cross-platform: C `long` ≠ C# `long` on 64-bit Unix.** Use `CLong` / `CULong` for `long`/`unsigned long` parameters of libc-shaped APIs.

## `[DllImport]` vs `[LibraryImport]`

| Aspect | `[DllImport]` (legacy) | `[LibraryImport]` (recommended) |
|---|---|---|
| Method modifier | `static extern` | `static partial` |
| Marshalling stub | IL stub generated at runtime (JIT) | C# source generated at compile time |
| AOT / trimming compatible | No | Yes |
| Debuggable marshalling | No | Yes (step into generated code) |
| String control | `CharSet` | `StringMarshalling` + `StringMarshallingCustomType` |
| Calling convention | `CallingConvention` field | `[UnmanagedCallConv]` attribute |
| `BestFitMapping` / `ThrowOnUnmappableChar` | Configurable | Removed (always `false`/`false`) |
| `ExactSpelling` | Configurable | Always exact (no `A`/`W` lookup) |
| `PreserveSig=false` | Supported | Removed (handle HRESULT manually) |
| `MarshalAs` | Full set | Subset (analyzer flags `SYSLIB1051`/`1052`) |
| `StringBuilder` | Supported | Not supported — use `Span<char>` / `ArrayPool<char>` |

### Canonical `[LibraryImport]`

```csharp
using System.Runtime.InteropServices;

internal static partial class User32
{
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}

[LibraryImport("nativelib", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
internal static partial int parse_thing(string input);
```

Calling convention on `[LibraryImport]` is **always** specified via `[UnmanagedCallConv]`. The wrapper type must be `partial`; the methods are `static partial` (no `extern`).

## Strings

| Marshalling | Native encoding | Pinned (zero-copy)? |
|---|---|---|
| `StringMarshalling.Utf16` / `[MarshalAs(UnmanagedType.LPWStr)]` / `CharSet.Unicode` | UTF-16 `wchar_t*` | Yes (by-value param) |
| `StringMarshalling.Utf8` / `[MarshalAs(UnmanagedType.LPUTF8Str)]` | UTF-8 `char*` | No |
| `[MarshalAs(UnmanagedType.LPStr)]` | ANSI on Win, UTF-8 on Unix | No |
| `[MarshalAs(UnmanagedType.BStr)]` | COM `BSTR` (length-prefixed; `SysFreeString`) | No |

`StringMarshalling.Custom` + `StringMarshallingCustomType = typeof(MyMarshaller)` for non-UTF encodings (no built-in ANSI on `[LibraryImport]`). `StringBuilder` is **not** supported by `[LibraryImport]` — use `Span<char>` / `byte[]` from `ArrayPool` with `[Out]`. **Never use `[Out] string`** — corrupts interned strings.

## Default type marshalling (runtime marshalling on)

| C# | Native |
|---|---|
| `byte`/`sbyte` ... `long`/`ulong`, `float`, `double` | Same-width primitive |
| `nint` / `nuint` | `intptr_t` / `uintptr_t` |
| `bool` | Win32 `BOOL` (4 bytes) — for C99 `_Bool`/`bool` use `[MarshalAs(UnmanagedType.U1)]` |
| `char` | Per `CharSet` / `StringMarshalling` |
| `Guid` | Win32 `GUID` (blittable) |
| `decimal` | COM `DECIMAL` |
| `string` (param) | `char*` / `char16_t*` per encoding |
| Array | Pointer to contiguous elements (default `[In]` only) |
| `[StructLayout]` class as param/field | Pointer / inlined struct |
| Delegate | Function pointer (`Marshal.GetFunctionPointerForDelegate`) |
| `SafeHandle`-derived | `void*` |

## Blittable types

Blittable types share the bit-level representation of their native counterpart, so the marshaller pins instead of copying. Blittable: integral primitives, floats, pointers, `nint`/`nuint`, `Guid`, structs with `LayoutKind.Sequential|Explicit` and all-blittable fields. **Not blittable: `bool`.** **Sometimes blittable: `char`** (1-D array or `CharSet=Unicode` struct), `string` (Utf16/`LPWSTR`/Unicode and by-value).

`[assembly: DisableRuntimeMarshalling]` makes every C# `unmanaged` type (no `LayoutKind.Auto` field) blittable; everything else fails to compile in a P/Invoke signature. In this mode `bool` is 1-byte unnormalized, `char` is always 2 bytes, `CharSet` is ignored; `SetLastError` / `BestFitMapping` / `ThrowOnUnmappableChar` / `LCIDConversion` / varargs / `in/ref/out` are unsupported.

Validate at runtime: `try { var h = GCHandle.Alloc(new MyStruct(), GCHandleType.Pinned); h.Free(); } catch (ArgumentException) { /* not blittable */ }`.

## Struct layout

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Unicode, Size = 64)]
public struct MyStruct { public int A; public int B; }
```

| Field | Behavior |
|---|---|
| `LayoutKind.Sequential` | Fields in declared order — **default for structs** |
| `LayoutKind.Explicit` | Each field needs `[FieldOffset(n)]` — for unions or precise layouts |
| `LayoutKind.Auto` | CLR may reorder — **default for classes; forbidden in interop** |
| `Pack` | 1, 2, 4, 8, 16 — equivalent to native `#pragma pack` |
| `Size` | Force minimum total size (padding added) |

Class default is `LayoutKind.Auto` — must be set explicitly to `Sequential` for interop. Union pattern: declare overlapping fields with `[FieldOffset(0)]` inside an `Explicit` struct.

Fixed-size buffers (allowed element types: `bool byte sbyte short ushort int uint long ulong char float double`; always 1-D, always struct instance fields, always inside `unsafe`):

```csharp
internal unsafe struct SYSTEM_PROCESS_INFORMATION
{
    internal uint NextEntryOffset;
    internal uint NumberOfThreads;
    private fixed byte Reserved1[48];
    internal UNICODE_STRING ImageName;
}
```

`fixed char[N]` is always 2 bytes/char regardless of `CharSet`. For inline buffers of non-primitives use `[MarshalAs(UnmanagedType.ByValArray, SizeConst = N)]`.

Boolean fields: default `bool` ⇒ Win32 `BOOL` (4-byte); C/C++ `bool` (1-byte) → `[MarshalAs(UnmanagedType.U1)] bool`; `VARIANT_BOOL` (2-byte) → `[MarshalAs(UnmanagedType.VariantBool)] bool`.

## Arrays

```csharp
// pointer + element conversion
[MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr, SizeParamIndex = 1)]
string[] names

// inline (struct field only)
[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
int[] inline16

// COM SAFEARRAY
[MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)]
string[] comNames
```

`SizeParamIndex` (zero-based) names the parameter that supplies the count. Array params are `[In]` by default — add `[Out]` to propagate callee mutations back to the managed caller.

## Custom marshallers (source-generated)

Driver attributes (`System.Runtime.InteropServices.Marshalling`): `[CustomMarshaller(Type managedType, MarshalMode mode, Type marshallerType)]` registers an entry-point/impl; `[NativeMarshalling(typeof(MyMarshaller))]` sets the type-default; `[MarshalUsing(typeof(MyMarshaller))]` overrides per-parameter / per-return / per-element; `[ContiguousCollectionMarshaller]` flags a generic collection marshaller (takes one extra type parameter for the unmanaged element).

`MarshalMode` values: `Default`, `ManagedToUnmanagedIn|Ref|Out`, `UnmanagedToManagedIn|Ref|Out` (reverse P/Invoke), `ElementIn|Ref|Out` (collection elements — stateless only).

Stateless marshaller shape — a `static` class with `ConvertToUnmanaged(managed)`, `ConvertToManaged(unmanaged)`, `Free(unmanaged)` (pick the methods needed for the registered `MarshalMode`). Stateful shape — a `struct` with `FromManaged(...)`, `ToUnmanaged()`, `Free()`, optional `OnInvoked()` (after the native call) and `GetPinnableReference()` (pin support).

Built-ins ready to use: `Utf8StringMarshaller`, `Utf16StringMarshaller`, `AnsiStringMarshaller`, `BStrStringMarshaller`, `ArrayMarshaller<T,TUnmanaged>`, `PointerArrayMarshaller<T,TUnmanaged>`, `SpanMarshaller<T,TUnmanaged>`, `ReadOnlySpanMarshaller<T,TUnmanaged>`, `SafeHandleMarshaller<T>`.

```csharp
[NativeMarshalling(typeof(ExampleMarshaller))]   // default for the type
public struct Example { public string Message; public int Flags; }

[LibraryImport("nativelib")]
[return: MarshalUsing(typeof(OtherMarshaller))]   // override return only
internal static partial Example Convert(Example e);
```

## Calling conventions

| Arch | Default |
|---|---|
| Windows x86 | `Stdcall` |
| Linux/macOS x86 | `Cdecl` |
| x64, ARM, ARM64 (all OSes) | single conv — attribute is no-op |

Modifier types in `System.Runtime.CompilerServices`: `CallConvCdecl`, `CallConvStdcall`, `CallConvThiscall`, `CallConvFastcall`, `CallConvSuppressGCTransition`, `CallConvMemberFunction`.

```csharp
[LibraryImport("kernel32.dll")]
[UnmanagedCallConv(CallConvs = [typeof(CallConvSuppressGCTransition)])]
internal static partial ulong GetTickCount64();

[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
private static int Cb(int x) => x;

// Function pointer with combined modifiers
delegate* unmanaged[Cdecl, MemberFunction]<int> p;
```

`SuppressGCTransition` removes the GC transition — only safe for trivial, fast, non-blocking native functions (no managed callbacks, no GC interaction). Unix-origin libs ported to Windows often keep `Cdecl`; explicit `[UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]` is mandatory for them on Win-x86.

## `unsafe` C#

`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` is required by `[LibraryImport]`. Pointer surface: `T*`, `void*`, `T**`, `*p`, `p->Field`, `p[i]`, `&x`, arithmetic, `sizeof(T)` for any unmanaged `T`.

```csharp
unsafe void M(byte[] src, byte[] dst, int n)
{
    fixed (byte* ps = src, pd = dst)
        for (int i = 0; i < n; i++) pd[i] = ps[i];
}
```

`fixed` works on arrays, `string` (gets `char*`), or `&field` of a managed struct — block-scoped pin; never let pointers escape. `stackalloc` allocates on the call stack (lifetime = enclosing method); cap with `if (n <= 256) stackalloc else ArrayPool`.

Pointer-free helpers (no `unsafe` keyword needed): `System.Runtime.CompilerServices.Unsafe` (`As<T,U>`, `AsRef<T>`, `Add`, `SizeOf<T>`, `ReadUnaligned`, `WriteUnaligned`, `SkipInit<T>`); `System.Runtime.InteropServices.MemoryMarshal` (`AsBytes`, `Cast<TFrom,TTo>`, `CreateSpan`, `GetArrayDataReference`, `Read<T>`, `AsRef<T>(Span<byte>)`).

## Function pointers (`delegate*`)

Compiled to `calli` — no allocation, no virtual dispatch (vs delegates which use `callvirt` on `Invoke`). Only inside `unsafe` context.

```csharp
delegate*<int, int, int> add;                                 // managed (default)
delegate* unmanaged[Cdecl]<int, int, int> addCdecl;            // unmanaged + specific conv
delegate* unmanaged[Stdcall]<int, int, int> addStd;
delegate* unmanaged<int, int, int> addDefault;                 // platform default
unsafe { static int Mul(int a, int b) => a * b; var p = &Mul; }
```

Rules: `&` only on `static` methods; `unmanaged` requires the target be `[UnmanagedCallersOnly]` (otherwise managed conv); cast to/from `IntPtr` via `(IntPtr)p` / `(delegate* …)ptr`.

## `[UnmanagedCallersOnly]` (reverse P/Invoke)

```csharp
[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)], EntryPoint = "do_double")]
public static int DoubleInt(int i) => i * 2;
```

Restrictions:
- `static`. Not callable from managed code.
- All parameter and return types **blittable**.
- No generic type parameters; not in a generic class.
- `EntryPoint` (optional) + NativeAOT publish ⇒ exported as a public C entry point.
- Exceptions that propagate out **terminate the process** — catch at the boundary and convert to error code.

For instance-capturing or non-static callbacks, use a `delegate` + `Marshal.GetFunctionPointerForDelegate` and root the delegate (static field or `GC.KeepAlive`) — the function pointer does **not** root the delegate.

## NativeAOT exports

In AOT-published assemblies, `[UnmanagedCallersOnly(EntryPoint = "...")]` methods are exported as public C symbols. Only methods compiled into the published assembly are exported (not those in project references / NuGet packages).

```xml
<PropertyGroup>
  <OutputType>Library</OutputType>
  <PublishAot>true</PublishAot>
  <NativeLib>Shared</NativeLib>     <!-- or Static -->
</PropertyGroup>
```

`dotnet publish -r <rid>` produces `lib*.so` / `*.dylib` / `*.dll` + `.lib`/`.a`.

Direct P/Invoke from AOT apps (bypasses the runtime resolver — `DefaultDllImportSearchPaths` is **not** honored; OS dynamic-loader rules apply):

```xml
<ItemGroup>
  <DirectPInvoke Include="__Internal" />              <!-- statically linked -->
  <DirectPInvoke Include="libc" />                    <!-- whole module -->
  <DirectPInvoke Include="kernel32!Sleep" />          <!-- single export -->
  <NativeLibrary Include="Dependency.lib" Condition="$(RuntimeIdentifier.StartsWith('win'))" />
  <NativeLibrary Include="Dependency.a"   Condition="!$(RuntimeIdentifier.StartsWith('win'))" />
</ItemGroup>
```

Trade-off: AOT runtime resolution (default; flexible, slower, cannot statically link) vs direct (fast, can static-link, fails fast if missing at startup).

## `NativeLibrary` API and resolver hooks

```csharp
NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, (name, asm, search) =>
{
    if (name == "nativedep" && Avx2.IsSupported)
        return NativeLibrary.Load("nativedep_avx2", asm, search);
    return IntPtr.Zero;   // fall through to default
});
```

- One resolver per assembly. Set before any P/Invoke into that assembly fires.
- `AssemblyLoadContext.ResolvingUnmanagedDll` is the per-ALC alternative.
- Library-name probing: Windows tries `name`, `name.dll`; Linux tries `name.so`, `libname.so`, `name`, `libname`; macOS likewise with `.dylib`. Absolute paths are taken as-is.

`[DefaultDllImportSearchPaths]` (assembly or method): flags `LegacyBehavior`, `AssemblyDirectory`, `UseDllDirectoryForDependencies`, `ApplicationDirectory`, `UserDirectories`, `System32`, `SafeDirectories`, `UserEvironmentPath`. Honored on Windows; ignored by AOT direct P/Invoke.

## Object lifetime and pinning

| Tool | Use case |
|---|---|
| `SafeHandle` | **Preferred** for OS handles — reliable cleanup, ref count, prevents handle-recycling attacks |
| `CriticalHandle` | Lighter than `SafeHandle`, no ref count, no thread-affinity |
| `GCHandle.Alloc(o, GCHandleType.Pinned)` | Pin across native calls; **must** `Free()` |
| `GCHandle.Alloc(o)` | Normal handle; pass `GCHandle.ToIntPtr(h)` to native, retrieve back |
| `GC.KeepAlive(o)` | Force liveness — critical for `Marshal.GetFunctionPointerForDelegate` |
| `fixed` | Block-scoped pin in `unsafe` code |
| `Marshal.AllocHGlobal` / `FreeHGlobal` | Process-heap allocations |
| `Marshal.AllocCoTaskMem` / `FreeCoTaskMem` | COM task allocator |
| `NativeMemory.Alloc/AllocZeroed/Free/Realloc/AlignedAlloc` | Modern allocator API |

```csharp
GCHandle h = GCHandle.Alloc(myObj);
NativeFunc(callback, GCHandle.ToIntPtr(h));
// In callback: var managed = GCHandle.FromIntPtr(p).Target;
h.Free();   // after last callback
```

GC tuning (Server GC, finalizer ordering, weak refs through native code) → load `dotnet-garbage-collection`.

## COM interop

### Built-in (Windows-only, runtime IL-gen, NOT AOT-compatible)

```csharp
[ComImport, Guid("xxxx-..."), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IFoo { void Method(int i); }
```

RCW (COM → managed): `Marshal.GetObjectForIUnknown(ptr)`. CCW (managed → COM): `[ComVisible(true)]` class + `Marshal.GetIUnknownForObject(o)`. CCW always exposes `IUnknown`, `IDispatch`, `IErrorInfo`, `ISupportErrorInfo`, `IProvideClassInfo`. Use `[ClassInterface(ClassInterfaceType.None)]` — force explicit interfaces.

### Source-generated (`ComWrappers`, .NET 8+, AOT-compatible)

```csharp
[GeneratedComInterface, Guid("3faca0d2-e7f1-4e9c-82a6-404fd6e0aab8")]
internal partial interface IFoo { void Method(int i); }

[GeneratedComClass]
internal partial class Foo : IFoo { public void Method(int i) { /* ... */ } }

ComWrappers cw = new StrategyBasedComWrappers();
IFoo foo = (IFoo)cw.GetOrCreateObjectForComInstance(native, CreateObjectFlags.None);
nint p   = cw.GetOrCreateComInterfaceForObject(new Foo(), CreateComInterfaceFlags.None);
```

Differences vs built-in:
- Interface and class both `partial`.
- Only `IUnknown`-based interfaces; `Dual` and `IDispatch` unsupported.
- All params default to `[In]` (built-in defaults arrays to `[In, Out]`).
- `[In]` / `[Out]` allowed only on arrays; otherwise use `in` / `out` modifiers.
- `[PreserveSig]` opts out of HRESULT→exception translation; method must return `int`.
- Implicit HRESULT translation: last `out` param can become C# return; non-void return is appended as `_Out_ T*` in the native sig.
- `Marshal.GetObjectForIUnknown` / `GetIUnknownForObject` are **not compatible** — use `ComWrappers` methods.

Implicit HRESULT mapping:

```csharp
void Method1(int i);              // HRESULT Method1(int i);
int  Method2(float f);            // HRESULT Method2(float f, _Out_ int* ret);
[PreserveSig] int Method3(int i, out int j);   // HRESULT Method3(int i, int* j);
```

Exposing managed to COM: set `<EnableComHosting>true</EnableComHosting>` (+ optional `<EnableRegFreeCom>true</EnableRegFreeCom>`); decorate the class with `[ComVisible(true)] [Guid("...")] [ClassInterface(ClassInterfaceType.None)]`. Register with `regsvr32 ProjectName.comhost.dll`. Limitations: framework-dependent only, no self-contained hosting; "Any CPU" comhost defaults to 64-bit; C++/CLI cannot use `EnableComHosting`. .NET 8+ needs `<RuntimeHostConfigurationOption Include="System.Runtime.InteropServices.COM.LoadComponentInDefaultContext" Value="true" />` for default-ALC loading.

## Exception interop

- **Windows-only** for unmanaged exception interop. On non-Windows the Unix ABI does not standardize unwinding — managed/native exceptions across the boundary lead to UB.
- `setjmp`/`longjmp` over managed frames: **not supported**.
- HRESULT mapping (when `PreserveSig=false` / default in `GeneratedComInterface`): non-success HRESULTs become typed `Exception` subclasses via `Marshal.GetExceptionForHR`. Common: `E_NOINTERFACE`→`InvalidCastException`, `E_INVALIDARG`→`ArgumentException`, `E_OUTOFMEMORY`→`OutOfMemoryException`, `E_NOTIMPL`→`NotImplementedException`, `RPC_E_CHANGED_MODE`→`InvalidOperationException`, otherwise `COMException` with `HResult`.
- On exception thrown across CCW back to a COM caller: runtime calls `IErrorInfo::SetErrorInfo` and returns the HRESULT. Use `Marshal.GetHRForException` to set HRESULT manually.
- Exceptions out of `[UnmanagedCallersOnly]` terminate the process (CoreCLR). Catch at the boundary:

```csharp
[UnmanagedCallersOnly(EntryPoint = "do_work")]
public static int DoWork(IntPtr ctx)
{
    try   { /* ... */ return 0; }
    catch (Exception ex) { return ex.HResult; }
}
```

## Cross-platform pitfalls

| Issue | Detail |
|---|---|
| Library naming | Win `name.dll`; Linux `libname.so` (often `.so.N`); macOS `libname.dylib`. Probe order is platform-specific. |
| C/C++ `long` | 32 bits everywhere except 64-bit Linux/macOS (LP64). Use `CLong` / `CULong` for libc-shaped APIs (`Func(new CLong(10))`, `result.Value`). |
| `wchar_t` | 2 bytes on Windows, typically 4 bytes on Unix. Avoid in cross-platform APIs — prefer UTF-8. |
| Calling-convention asymmetry | Only Windows x86 distinguishes `Cdecl` vs `Stdcall`. Unix-origin libs ported to Windows often keep `Cdecl` — explicit `[UnmanagedCallConv(...)]`. |
| Struct alignment | MSVC and clang/gcc differ subtly. Match `Pack` to native `#pragma pack`; pin enum width with `enum X : byte`; 64-bit `double` alignment is 8 on x64 SysV, 4 on Win32, 8 on Win64. |
| Apartment threading | `[STAThread]` / `[MTAThread]` is Windows-only. Most .NET threads default to MTA. |

## Common Windows types (selected)

`BOOL`→`int`(or `bool`); `BYTE`/`UCHAR`→`byte`; `DWORD`/`ULONG`/`UINT`→`uint`; `QWORD`/`ULONGLONG`→`ulong`; `HRESULT`/`NTSTATUS`→`int`; `HANDLE`/`HWND`/`LPARAM`/`LRESULT`/`LONG_PTR`→`IntPtr`/`nint`; `WPARAM`/`UINT_PTR`/`SIZE_T`→`UIntPtr`/`nuint`; `PVOID`→`void*` (preferred) or `IntPtr`.

## Troubleshooting matrix

| Symptom | Likely cause | Fix |
|---|---|---|
| `DllNotFoundException` | Library not on search path / wrong RID | Verify name, path; `NativeLibrary.TryLoad`; `LD_LIBRARY_PATH` / `rpath` on Linux. |
| `EntryPointNotFoundException` | Mangled name / missing `extern "C"` / `A`/`W` mismatch | `dumpbin /exports` (Win), `nm -D` (Linux). Set explicit `EntryPoint`; `ExactSpelling=true`. |
| `AccessViolationException` | Sig mismatch / use-after-free / unpinned object | Compare native vs managed sig; `Marshal.SizeOf<T>()` vs native `sizeof`; verify lifetime. |
| Silent data corruption | Wrong size or encoding | Boundary logging; size assertions; round-trip test. |
| Intermittent crashes | GC moved/collected callback | Root the delegate (static field / `GC.KeepAlive`); `fixed`/`GCHandle` for cross-call pointers. |
| Heap corruption on free | Allocator mismatch | Use the library's own `free` symbol; never mix `malloc/CoTaskMemFree`. |
| Bad calling convention | Mismatched conv on Win-x86 | Check `Stdcall` vs `Cdecl` in headers. |
| `SafeHandle` always 0 | Missing `out` / wrong native sig (returns by-value vs by-pointer) | Use `[DllImport]` with `out SafeXxx`; ensure `IsInvalid` reflects sentinel. |

## Source-generator diagnostics

`Microsoft.Interop.LibraryImportGenerator`: `SYSLIB1050` (invalid `[LibraryImport]` usage) → `SYSLIB1062` (`<AllowUnsafeBlocks>` required). The most common ones: `SYSLIB1054` (info-level — migrate `[DllImport]` to `[LibraryImport]`), `SYSLIB1051` (type not supported by source-gen), `SYSLIB1052` (configuration not supported).

`Microsoft.Interop.ComInterfaceGenerator` adds analogous `SYSLIB1090+` diagnostics for `[GeneratedComInterface]` / `[GeneratedComClass]`.

## Migration cheat-sheet — `[DllImport]` → `[LibraryImport]`

| `[DllImport]` field | `[LibraryImport]` equivalent |
|---|---|
| `static extern` | `static partial` |
| `CharSet = CharSet.Unicode` | `StringMarshalling = StringMarshalling.Utf16` |
| `CharSet = CharSet.Ansi` (Win) | `StringMarshalling = StringMarshalling.Custom` + custom marshaller, or `[MarshalAs(UnmanagedType.LPStr)]` per-param |
| `CharSet = CharSet.Ansi` (Unix) | `StringMarshalling = StringMarshalling.Utf8` |
| `CallingConvention = X` | `[UnmanagedCallConv(CallConvs = [typeof(CallConvX)])]` |
| `BestFitMapping=false`, `ThrowOnUnmappableChar=false` | implicit (always) |
| `ExactSpelling=true` | implicit (always) |
| `PreserveSig=false` | not supported — handle HRESULT manually |
| `StringBuilder` param | `Span<char>` / `byte[]` from `ArrayPool` + `[Out]` |

## End-to-end example — UTF-8 string + struct + callback

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct Stat { public uint Mode; public ulong Size; /* ... */ }

internal static partial class Libc
{
    [LibraryImport("libc.so.6", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int ftw(
        string dirpath,
        delegate* unmanaged[Cdecl]<byte*, Stat*, int, int> cb,
        int descriptors);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe int Display(byte* fName, Stat* st, int typeFlag)
    {
        Console.WriteLine(Marshal.PtrToStringUTF8((IntPtr)fName));
        return 0;
    }

    public static unsafe void Run() => ftw(".", &Display, 10);
}
```

## Quick decision matrix

| Question | Answer |
|---|---|
| New P/Invoke on .NET 7+ | `[LibraryImport]` (containing type `partial`, `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`) |
| String parameter | Set `StringMarshalling.Utf8` / `Utf16` explicitly — never default ANSI |
| Native callback | `[UnmanagedCallersOnly]` (static) → `delegate* unmanaged[...]` from native side; or `Marshal.GetFunctionPointerForDelegate` + root the delegate |
| Allocate native memory | `NativeMemory.Alloc/Free`; or library's own allocator if it gave you the buffer |
| Hold an OS handle | `SafeHandle`-derived class — never raw `IntPtr` + finalizer |
| Pin a buffer for the duration of a call | `fixed` (block-scoped) |
| Pin across multiple calls | `GCHandle.Alloc(o, GCHandleType.Pinned)` — `Free()` when done |
| Expose a managed entry point to a C consumer | `[UnmanagedCallersOnly(EntryPoint="...")]` + `<PublishAot>true</PublishAot>` + `<NativeLib>Shared</NativeLib>` |
| COM, AOT-compatible | `[GeneratedComInterface]` / `[GeneratedComClass]` + `StrategyBasedComWrappers` |
| COM, Windows-only legacy | `[ComImport]` + `Marshal.GetObjectForIUnknown` |
| Switch the library at load time per CPU/feature | `NativeLibrary.SetDllImportResolver` (one per assembly) |
| Skip the GC transition for a trivial fast call | `[UnmanagedCallConv(CallConvs = [typeof(CallConvSuppressGCTransition)])]` |

## Cross-references

- Public docs (Native interop overview): https://learn.microsoft.com/en-us/dotnet/standard/native-interop/
- Public docs (P/Invoke): https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke
- Public docs (LibraryImport source generator): https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation
- Public docs (Custom marshalling source-gen): https://learn.microsoft.com/en-us/dotnet/standard/native-interop/custom-marshalling-source-generation
- Public docs (Calling conventions): https://learn.microsoft.com/en-us/dotnet/standard/native-interop/calling-conventions
- Public docs (`DisableRuntimeMarshalling`): https://learn.microsoft.com/en-us/dotnet/standard/native-interop/disabled-marshalling
- Public docs (Best practices): https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices
- Public docs (`unsafe`, fixed buffers): https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code
- Public docs (Function pointers `delegate*`): https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/function-pointers
- Public docs (`[UnmanagedCallersOnly]`): https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.unmanagedcallersonlyattribute
- Public docs (NativeAOT interop & exports): https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop
- Public docs (NativeLibrary / `DllImportResolver`): https://learn.microsoft.com/en-us/dotnet/standard/native-interop/native-library-loading
- Public docs (Exception interop): https://learn.microsoft.com/en-us/dotnet/standard/native-interop/exceptions-interoperability
- Public docs (`ComWrappers` source-gen): https://learn.microsoft.com/en-us/dotnet/standard/native-interop/comwrappers-source-generation
- Public docs (`EnableComHosting`): https://learn.microsoft.com/en-us/dotnet/core/native-interop/expose-components-to-com
- Public docs (SYSLIB1050-1069 diagnostics): https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib1050-1069
- Related skill: `dotnet-garbage-collection` — pinning costs, finalizer order, weak refs through native code.
- Related skill: `dotnet-io` — `Stream`, memory-mapped files, `Span<byte>` buffers that back native calls.
- Related skill: `dotnet-conventions` § csharp-style — team rules on `unsafe` use and blittable structs.
