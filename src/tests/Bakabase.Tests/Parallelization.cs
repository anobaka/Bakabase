using Microsoft.VisualStudio.TestTools.UnitTesting;

// Keep tests in the same class sequential: their fixtures and temporary resources are
// often reused across methods. Independent classes can run on two workers.
[assembly: Parallelize(Workers = 2, Scope = ExecutionScope.ClassLevel)]
