# Ixian S2

**Ixian S2** is the decentralized, trustless **data transmission and streaming layer** of the [Ixian Platform](https://www.ixian.io).
It enables **real-time communication, presence-based discovery, and streaming services** on top of [Ixian DLT](https://github.com/ixian-platform/Ixian-DLT).

S2 is designed for the next generation of decentralized applications, from **secure messaging** to **IoT networks**,
with built-in support for **scalable presence** and **fair monetization models**.

---

## 🚀 Why Ixian S2?

Ixian S2 is built to replace centralized communication servers with a **decentralized overlay network** that scales.

* 🕸️ **Presence-based Communication** - Users and devices are discovered via **cryptographic addresses**, not IP or DNS.
  Presence packets (signed and time-limited) ensure authenticity and freshness without SSL/TLS or certificate authorities.

* 🕊️ **Starling Presence Scaling Model** - A sector-based scaling architecture where S2 nodes are grouped into relay sectors.
  This enables efficient lookups, low overhead, and scalability to trillions of devices.

* 💰 **Incentives for Node Operators** - S2 nodes are economically incentivized.
  Operators earn **relay rewards** for transactions they forward into the DLT network, via a **dual fee model** (DLT network fee + S2 relay fee).
  This ensures fair distribution, aligned incentives, and long-term sustainability.

* 🔒 **Secure & Trustless** - Every connection is authenticated cryptographically.
  Identities, presence, and discovery are self-authenticated, making external trust systems unnecessary.

* ⚡ **Low Latency, High Throughput** - Optimized for **real-time streaming, messaging, and dApps**, even under global load.

* ♻️ **Resilient by Design** - Decentralized, fault-tolerant architecture with no downtime or single points of failure.

---

## 🔎 How Client Discovery Works in Ixian

Unlike traditional networks where discovery relies on DNS/IP, SSL certificates and central directories, Ixian uses
**cryptographic addresses** and **presence packets**. This eliminates the need for external trust systems.

**Lifecycle of a client lookup:**

1. **Initiation** - User A wants to connect to User B, knowing only B's **Ixian cryptographic address**.
2. **Sector Resolution** - User A sends a *Get Sector* request to its connected S2 node. The S2 node queries the **Ixian DLT**
to determine which sector User B belongs to (DLT tracks all active S2 base presences).
3. **Presence Request** - User A queries one of the S2 nodes in User B's sector for User B's **presence packet**.
4. **Presence Packet** - If User B is online, the S2 node returns B's signed presence packet, which contains:

   * Timestamp (to prevent replay and confirm freshness),
   * Contact details (IP/transport endpoint, with support for non-IP in the future),
   * Signature from User B's private key proving authenticity.
5. **Communication Setup** - User A now knows how to communicate with User B:

   * Directly (if reachable and if User B chooses to reveal their direct communication channel publicly),
   * Or indirectly (via relay/TURN functionality provided by S2 nodes if behind NAT/firewall).
6. **Keep-alive cycle** - User B must refresh its presence every few minutes with a new timestamp + signature, or the presence
expires automatically.

---

### ✅ Why It Matters

* 🔑 **No centralized SSL/TLS infrastructure required** - trust comes from cryptographic signatures, not certificate authorities.
* 🕸️ **Lookup by cryptographic address** - eliminates DNS and central directories.
* ⚡ **Scalable by design** - the Starling model and sectorized presence make discovery efficient, even at **trillion-device
scale**.
* 🔒 **Always authentic, always fresh** - signatures + expiry guarantee that presence data can't be spoofed or reused.
* ♻️ **Future-ready** - supports both Internet-based IP and non-IP addressing models.

**Ixian S2 turns discovery and communication into a cryptographically verifiable, decentralized service - future-proof by
design.**

---

## 📚 Documentation

👉 Full build guides, API references, and integration docs are available at:
[https://docs.ixian.io](https://docs.ixian.io)

---

## 🔗 Related Repositories

* [Ixian-Core](https://github.com/ixian-platform/Ixian-Core) - SDK and shared functionality
* [Ixian-DLT](https://github.com/ixian-platform/Ixian-DLT) - Blockchain ledger and consensus layer
* [Ixian-S2](https://github.com/ixian-platform/Ixian-S2) - Decentralized streaming & messaging (this repository)
* [Spixi](https://github.com/ixian-platform/Spixi) - Secure messenger and wallet app
* [QuIXI](https://github.com/ixian-platform/QuIXI) - Quick integration toolkit for Ixian Platform

---

## 🌱 Development Branches

* **master** - Stable, production-ready releases
* **development** - Active development, may contain unfinished features

For reproducible builds, always use the latest **release tag** on `master`.

---

## 🤝 Contributing

We welcome contributions from developers, integrators, and builders.

1. Fork this repository
2. Create a feature branch ('feature/my-change')
3. Commit with clear messages
4. Open a Pull Request for review

Join the discussion on **[Discord](https://discord.gg/pdJNVhv)**.

---

## 🌍 Community & Links

* **Website**: [www.ixian.io](https://www.ixian.io)
* **Docs**: [docs.ixian.io](https://docs.ixian.io)
* **Discord**: [discord.gg/pdJNVhv](https://discord.gg/pdJNVhv)
* **Telegram**: [t.me/ixian\_official\_ENG](https://t.me/ixian_official_ENG)
* **Bitcointalk**: [Forum Thread](https://bitcointalk.org/index.php?topic=4631942.0)
* **GitHub**: [ixian-platform](https://www.github.com/ixian-platform)

---

## 📜 License

Licensed under the [MIT License](LICENSE).
