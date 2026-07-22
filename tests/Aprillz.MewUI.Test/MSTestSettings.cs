// Tests run serially: the visual and logical trees are single-threaded, and their reused
// traversal scratch is a plain static, so concurrent test methods must not share it.
[assembly: DoNotParallelize]
