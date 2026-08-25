> [!WARNING]
> **Status Disclaimer:** Moonshine is in active development (v0.5.6-alpha). It is its own platform with its own protocol (MNBP v1), not a GameStream client or Moonlight replacement. No end-to-end streaming works yet. The application is fail-closed.

# Custom SIMD Galois Field GF(2^8) Reed-Solomon Forward Error Correction

Moonshine implements a custom-engineered, multi-tiered SIMD Galois Field $GF(2^8)$ Reed-Solomon Forward Error Correction (FEC) engine supporting AVX-512BW, AVX2, and 64-bit scalar execution paths. This engine serves Moonshine's native MNBP v1 framing for reliable delivery.

---

## 1. Mathematical Formulation

### A. Generator Polynomial
The Galois Field $GF(2^8)$ arithmetic in Moonshine uses the primitive generator polynomial:
$$P(x) = x^8 + x^4 + x^3 + x^2 + 1 \quad (\text{0x11D} \text{ / } \text{0x1D})$$

Multiplication of two elements $\alpha, \beta \in GF(2^8)$ corresponds to polynomial multiplication modulo $P(x)$ over $GF(2)$:
$$\alpha \cdot \beta = (\alpha(x) \times \beta(x)) \pmod{P(x)}$$

Logarithmic and exponential exponent tables are precomputed for $O(1)$ scalar multiplication:
$$\alpha \cdot \beta = \exp\Big((\log(\alpha) + \log(\beta)) \pmod{255}\Big)$$

---

## 2. Multi-Tiered SIMD Execution Hierarchy

```
┌────────────────────────────────────────────────────────┐
│               ReedSolomonSimd Dispatcher               │
└───────────────────────────┬────────────────────────────┘
                            │
       ┌────────────────────┼────────────────────┐
       ▼                    ▼                    ▼
┌───────────────┐    ┌───────────────┐    ┌───────────────┐
│ AVX-512BW     │    │  AVX2         │    │  Scalar /     │
│ Nibble Table  │    │  Nibble Table │    │  64-bit Tail  │
│ 64 Bytes/step │    │  32 Bytes/step│    │ Word Fallback │
└───────────────┘    └───────────────┘    └───────────────┘
```

### Tier 1: AVX-512BW 4-Bit Nibble Decomposition (64 Bytes Per Step)
On CPUs with `AVX512F` and `AVX512BW`, multiplication uses replicated 4-bit lookup tables with `_mm512_shuffle_epi8`, then XORs the low and high nibble results. No GFNI instruction is advertised or executed.

### Tier 2: AVX2 4-Bit Nibble Decomposition (32 Bytes Per Step)
On AVX2 hardware, each byte $b$ is decomposed into low nibble $b_L = b \ \& \ \text{0x0F}$ and high nibble $b_H = (b \gg 4) \ \& \ \text{0x0F}$:
$$b \cdot c = (b_L \cdot c) \oplus (b_H \cdot c)$$

- Lookups are executed simultaneously across 32 bytes using `_mm256_shuffle_epi8` (`vpshufb`).
- Results are recombined using `_mm256_xor_si256`.

---

## 3. Dynamic CPU Feature Detection

At runtime, the engine queries CPUID leaf 7:
- **AVX2**: `EBX` bit 5
- **AVX-512F**: `EBX` bit 16
- **AVX-512BW**: `EBX` bit 30

The active instruction set can be queried via `moonshine_fec_get_simd_architecture()`.
