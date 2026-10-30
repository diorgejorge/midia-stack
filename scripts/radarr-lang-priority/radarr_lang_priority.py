#!/usr/bin/env python3
"""Prioritize releases with audio in a preferred language on Radarr.

Commands:
  setup   Create/update a Custom Format for the language and score it in the
          quality profiles, so Radarr prefers and upgrades to that audio.
  search  Trigger searches for movies whose current file lacks the language
          (Radarr has no periodic search; RSS alone only catches new posts).

Configuration comes from environment variables (see README.md).
Only the Python standard library is used.
"""
import json
import os
import sys
import time
import urllib.request
import xml.etree.ElementTree as ElementTree
from pathlib import Path

RADARR_URL = os.environ.get("RADARR_URL", "http://localhost:7878").rstrip("/")
CONFIG_XML = Path(os.environ.get(
    "RADARR_CONFIG", Path(__file__).resolve().parents[2] / "config" / "radarr" / "config.xml"))
LANGUAGE_NAMES = [n.strip() for n in os.environ.get(
    "LANG_NAMES", "Portuguese (Brazil)").split(",") if n.strip()]
TITLE_REGEX = os.environ.get(
    "TITLE_REGEX", r"\b(DUAL|DUBLADO|NACIONAL|PT[-. ]?BR)\b")
CF_NAME = os.environ.get("CF_NAME", "Preferred Audio Language")
CF_SCORE = int(os.environ.get("CF_SCORE", "1000"))
PROFILE_NAMES = {n.strip() for n in os.environ.get("PROFILES", "").split(",") if n.strip()}
SEARCH_LIMIT = int(os.environ.get("SEARCH_LIMIT", "10"))
COOLDOWN_SECONDS = int(os.environ.get("COOLDOWN_DAYS", "7")) * 86400
STATE_FILE = Path(os.environ.get("STATE_FILE", "radarr_lang_priority_state.json"))

ANY_LANGUAGE = {"id": -1, "name": "Any"}


def read_api_key():
    """Prefer RADARR_API_KEY; otherwise read it from Radarr's config.xml."""
    key = os.environ.get("RADARR_API_KEY")
    if key:
        return key
    try:
        return ElementTree.parse(CONFIG_XML).findtext("ApiKey", "")
    except (OSError, ElementTree.ParseError):
        return ""


API_KEY = read_api_key()


def api(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(
        f"{RADARR_URL}/api/v3/{path}", data=data, method=method,
        headers={"X-Api-Key": API_KEY, "Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=60) as response:
        payload = response.read()
    return json.loads(payload) if payload else None


def target_languages():
    wanted = {name.lower() for name in LANGUAGE_NAMES}
    found = [lang for lang in api("GET", "language") if lang["name"].lower() in wanted]
    if not found:
        sys.exit(f"None of {LANGUAGE_NAMES} exist in this Radarr instance.")
    return found


def build_custom_format(languages):
    specs = [{
        "name": f"Language {lang['name']}",
        "implementation": "LanguageSpecification",
        "negate": False,
        "required": False,
        "fields": [{"name": "value", "value": lang["id"]},
                   {"name": "exceptLanguage", "value": False}],
    } for lang in languages]
    specs.append({
        "name": "Release title",
        "implementation": "ReleaseTitleSpecification",
        "negate": False,
        "required": False,
        "fields": [{"name": "value", "value": TITLE_REGEX}],
    })
    return {"name": CF_NAME, "includeCustomFormatWhenRenaming": False,
            "specifications": specs}


def upsert_custom_format(languages):
    custom_format = build_custom_format(languages)
    existing = next((cf for cf in api("GET", "customformat") if cf["name"] == CF_NAME), None)
    if existing:
        custom_format["id"] = existing["id"]
        return api("PUT", f"customformat/{existing['id']}", custom_format)
    return api("POST", "customformat", custom_format)


def apply_score_to_profiles(format_id):
    for profile in api("GET", "qualityprofile"):
        if PROFILE_NAMES and profile["name"] not in PROFILE_NAMES:
            continue
        for item in profile["formatItems"]:
            if item["format"] == format_id:
                item["score"] = CF_SCORE
        profile["upgradeAllowed"] = True
        profile["cutoffFormatScore"] = max(profile.get("cutoffFormatScore", 0), CF_SCORE)
        profile["minUpgradeFormatScore"] = max(profile.get("minUpgradeFormatScore", 1), 1)
        # A fixed profile language (e.g. "Original") would reject dubbed releases.
        profile["language"] = ANY_LANGUAGE
        api("PUT", f"qualityprofile/{profile['id']}", profile)
        print(f"Profile '{profile['name']}': {CF_NAME} = {CF_SCORE}")


def setup():
    custom_format = upsert_custom_format(target_languages())
    print(f"Custom Format '{CF_NAME}' ready (id {custom_format['id']})")
    apply_score_to_profiles(custom_format["id"])


def load_state():
    try:
        return json.loads(STATE_FILE.read_text())
    except (FileNotFoundError, json.JSONDecodeError):
        return {}


def needs_upgrade(movie, language_ids):
    if not (movie.get("monitored") and movie.get("hasFile")):
        return False
    file_languages = movie.get("movieFile", {}).get("languages", [])
    return not any(lang["id"] in language_ids for lang in file_languages)


def search():
    language_ids = {lang["id"] for lang in target_languages()}
    state = load_state()
    now = time.time()
    candidates = [
        movie["id"] for movie in api("GET", "movie")
        if needs_upgrade(movie, language_ids)
        and now - state.get(str(movie["id"]), 0) >= COOLDOWN_SECONDS
    ]
    # Oldest searches first, so every movie gets its turn.
    candidates.sort(key=lambda movie_id: state.get(str(movie_id), 0))
    batch = candidates[:SEARCH_LIMIT]
    if not batch:
        print("Nothing to search.")
        return
    api("POST", "command", {"name": "MoviesSearch", "movieIds": batch})
    state.update({str(movie_id): now for movie_id in batch})
    STATE_FILE.write_text(json.dumps(state))
    print(f"Searching {len(batch)} of {len(candidates)} pending movies: {batch}")


COMMANDS = {"setup": setup, "search": search}

if __name__ == "__main__":
    if len(sys.argv) != 2 or sys.argv[1] not in COMMANDS or not API_KEY:
        sys.exit("Usage: radarr_lang_priority.py {setup|search} "
                 "(API key from RADARR_API_KEY or RADARR_CONFIG)")
    COMMANDS[sys.argv[1]]()
