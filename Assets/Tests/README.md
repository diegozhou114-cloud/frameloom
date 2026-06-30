# Tests Notes

- **Benchmark_ tests** always pass — they are benchmarks measuring throughput, not behavioral correctness.
- **DifferentSeed tests** can probabilistically flake due to hash collisions between different seed sequences.
- **LogicIntOverflowTests** contain no-op assertions; edge cases (MinValue, MaxValue, wraparound) are not covered.
