// Terminal state is per instance, so test classes may run in parallel.
[assembly: Parallelize(Scope = ExecutionScope.ClassLevel)]
