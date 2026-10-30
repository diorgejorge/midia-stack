@echo off
rem Busca periodica de releases com audio PT-BR. A API key vem do config\radarr\config.xml.
cd /d "%~dp0"
py radarr_lang_priority.py search >> search.log 2>&1
