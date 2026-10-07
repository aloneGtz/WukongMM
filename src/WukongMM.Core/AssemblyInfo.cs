using System.Runtime.CompilerServices;

// Cli 的 selftest / roundtrip 需要调用内部的强制重编码接口
[assembly: InternalsVisibleTo("wmm")]
