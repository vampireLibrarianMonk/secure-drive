# Start Here

Welcome to **Emergency Archive** — a way to keep your important documents
(IDs, insurance, wills, medical records) encrypted on a USB stick so that you,
or your family in an emergency, can find and open them with one password.

Pick the path that matches you:

---

## 🧑‍💻 I just want to use it (most people)

You do **not** need to know anything about programming. You will download a
file, put it on a USB stick, and open it.

➡️ **Go to the [User Guide](docs/USER-GUIDE.md).**

It walks you through, step by step and with pictures:

1. Getting a USB stick ready.
2. Downloading the app.
3. Putting it on the stick.
4. Setting your password.
5. Adding your documents and writing a letter for your family.
6. Finding a document later (including in an emergency).

---

## 👨‍🔧 I set this up for someone else (family helper / IT-savvy)

Same [User Guide](docs/USER-GUIDE.md) — plus, if you are comfortable with a
terminal, the [First-Use / Drive Setup guide](docs/FIRST-USE.md) shows the
one-command way to prepare a drive and how to reset one to start over.

---

## 🛠️ I want to build or contribute to the code (developers)

➡️ **Go to the [README](README.md)** for the developer overview, then
[docs/BUILD.md](docs/BUILD.md) to build and test.

Key developer docs:

| Doc | What it covers |
|---|---|
| [README.md](README.md) | Project overview, layout, build/test, CI |
| [docs/BUILD.md](docs/BUILD.md) | Environment, build, test, container build, publish |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Components and data flows |
| [docs/CRYPTOGRAPHY.md](docs/CRYPTOGRAPHY.md) | Vault format and crypto choices |
| [docs/THREAT-MODEL.md](docs/THREAT-MODEL.md) | What is protected, and what is not |
| [docs/RECOVERY.md](docs/RECOVERY.md) | Long-term, app-independent recovery |

---

## In one sentence

An app that runs straight from a USB stick, asks for one password, and gives
your family a simple search box to find your important documents — no internet,
no accounts, nothing installed on the computer.
