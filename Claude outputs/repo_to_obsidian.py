"""
repo_to_obsidian.py

Bygger en Obsidian-vault direkt från en kodrepo, utan externa beroenden.
Varje källfil blir en Markdown-notis med YAML-frontmatter, och filer som
refererar varandras klasser länkas ihop med [[wikilinks]] så att Obsidians
inbyggda grafvy kan rita upp beroendegrafen.

v2: varje notis får nu även `layer` (Api/Application/Domain/Infrastructure/
Worker, från mappnamnet) och `component_type` (Controller/Service/
Repository/Entity/Dto/...) i frontmatter, plus motsvarande taggar
(layer/xxx, type/xxx). Använd det för att:
- Färglägga/gruppera Graph View efter tagg (Inställningar > Graph view > Groups)
- Bygga Dataview-tabeller, t.ex.
  ```dataview
  TABLE layer, component_type FROM #atlas WHERE component_type = "Repository"
  ```

Kör: python repo_to_obsidian.py
"""

import os
import re
from pathlib import Path

# --- CONFIG ---
REPO_PATH = Path(r"C:\Users\Peter\Projects\ProjectAtlas")
VAULT_PATH = Path(r"D:\Claude\obsidian\Atlas\AtlasProject")

# Vilka filtyper som ska bli noter
INCLUDE_EXT = {".cs", ".json", ".bicep", ".yml", ".yaml", ".md", ".sql", ".csproj", ".sln"}
EXCLUDE_DIRS = {"bin", "obj", ".git", ".vs", "node_modules", "packages"}

USING_RE = re.compile(r'^\s*using\s+([\w.]+)\s*;', re.MULTILINE)
CLASS_RE = re.compile(r'\b(?:class|interface|record|struct)\s+(\w+)')


def note_name_for(rel_path: Path) -> str:
    """Obsidian-notisens titel = filnamnet utan ändelse."""
    return rel_path.stem


def detect_layer(rel_path: Path) -> str:
    """
    Plockar ut arkitekturlagret från mappnamnet, t.ex.
    src/Atlas.Application/Tickets/... -> "Application".
    Bygger på Clean Architecture-namngivningen (Atlas.Api/.Application/.Domain/
    .Infrastructure/.Worker) — justera prefixet om ditt repo heter något annat.
    """
    for part in rel_path.parts:
        if part.startswith("Atlas."):
            return part.split(".", 1)[1]
    return "Other"


def detect_type(rel_path: Path, note_name: str) -> str:
    """
    Enkel mapp-/namnbaserad heuristik för komponenttyp — bra nog för att
    gruppera och färglägga i Obsidians grafvy, inte en riktig AST-analys.
    """
    parts = rel_path.parts
    if rel_path.suffix == ".csproj":
        return "Project"
    if "Controllers" in parts:
        return "Controller"
    if "Migrations" in parts:
        return "Migration"
    if "Configurations" in parts:
        return "EfConfiguration"
    if "Repositories" in parts or note_name.endswith("Repository"):
        return "Repository"
    if "Entities" in parts:
        return "Entity"
    if "Dtos" in parts:
        return "Dto"
    if "Events" in parts:
        return "Event"
    if note_name.endswith(("Worker", "Consumer")):
        return "BackgroundService"
    if "Services" in parts or "Interfaces" in parts:
        # Interface-konventionen i .NET: "I" + versal, t.ex. ITicketService
        if len(note_name) > 1 and note_name[0] == "I" and note_name[1].isupper():
            return "Interface"
        return "ServiceImpl"
    return "Other"


def collect_files() -> list[Path]:
    found = []
    for root, dirs, files in os.walk(REPO_PATH):
        dirs[:] = [d for d in dirs if d not in EXCLUDE_DIRS]
        for f in files:
            p = Path(root) / f
            if p.suffix.lower() in INCLUDE_EXT:
                found.append(p)
    return found


def build_vault():
    VAULT_PATH.mkdir(parents=True, exist_ok=True)

    print("🔵 Läser Atlas-repot...")
    all_files = collect_files()
    print(f"   Hittade {len(all_files)} filer.")

    # Steg 1: läs in allt och bygg en karta klassnamn -> notisnamn,
    # så vi vet vart vi ska länka innan vi skriver några filer.
    print("🟣 Analyserar klasser och referenser...")
    file_info = {}
    class_to_note = {}
    for f in all_files:
        try:
            text = f.read_text(encoding="utf-8", errors="ignore")
        except Exception as e:
            print(f"   ⚠️  Kunde inte läsa {f}: {e}")
            continue

        rel = f.relative_to(REPO_PATH)
        classes = CLASS_RE.findall(text) if f.suffix == ".cs" else []
        note_name = note_name_for(rel)
        layer = detect_layer(rel)
        ctype = detect_type(rel, note_name)
        file_info[f] = {
            "text": text,
            "classes": classes,
            "note_name": note_name,
            "layer": layer,
            "type": ctype,
        }
        for c in classes:
            class_to_note[c] = note_name

    # Steg 2: skriv en .md-notis per fil, med länkar till andra filer
    # vars klassnamn nämns i källkoden (enkel heuristik, se förklaring i chatten).
    print("🟢 Exporterar till Obsidian-vault...")
    for f, info in file_info.items():
        rel = f.relative_to(REPO_PATH)
        note_path = VAULT_PATH / rel.with_suffix(".md")
        note_path.parent.mkdir(parents=True, exist_ok=True)

        links = {
            note
            for cls, note in class_to_note.items()
            if cls != info["note_name"] and re.search(rf'\b{re.escape(cls)}\b', info["text"])
        }

        lang = "csharp" if f.suffix == ".cs" else f.suffix.lstrip(".")

        layer_tag = info["layer"].lower()
        type_tag = info["type"].lower()
        lines = [
            "---",
            f'title: "{info["note_name"]}"',
            f'source_path: "{rel.as_posix()}"',
            f'layer: "{info["layer"]}"',
            f'component_type: "{info["type"]}"',
            f'tags: [atlas, code, {f.suffix.lstrip(".")}, "layer/{layer_tag}", "type/{type_tag}"]',
            "---",
            "",
            f"# {info['note_name']}",
            "",
        ]
        if links:
            lines.append("## Kopplade filer")
            lines.extend(f"- [[{link}]]" for link in sorted(links))
            lines.append("")

        lines.append(f"```{lang}")
        lines.append(info["text"])
        lines.append("```")

        note_path.write_text("\n".join(lines), encoding="utf-8")

    print("✅ Klart! Hela Atlas-repot finns nu i Obsidian.")
    print(f"📁 Exportmapp: {VAULT_PATH}")


if __name__ == "__main__":
    build_vault()
