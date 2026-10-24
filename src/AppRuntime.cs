namespace Yuqiao {
 // A manually launched single instance per Windows session. This class never
 // launches another program and never registers a background/startup service.
 internal static class AppRuntime {
#if GENERAL_EDITION
  internal const string MutexName=@"Local\YuqiaoTranslateGeneral";
#else
  internal const string MutexName=@"Local\YuqiaoTranslateStandalone";
#endif
 }
}
