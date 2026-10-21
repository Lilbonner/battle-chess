# Инструменты извлечения ресурсов

Скрипты, которыми из образа диска **Battle Chess для Macintosh (B&W 1.0.2)** получена папка `original`
для режима «Battle Chess». Ресурсы игры в репозиторий не входят — их можно получить только из своей копии игры.

> Это исследовательский набор, а не установщик «в один клик»: часть промежуточных файлов
> (`globals.bin` — восстановленные глобальные переменные A5, `combat_tables.pkl` — таблицы поединков T01/T23/T4,
> `bases.pkl`) собиралась вручную по ходу разбора. Форматы описаны в [docs/FORMAT.md](../docs/FORMAT.md).

## Зависимости
Python 3.11+ и пакеты:

```bash
pip install machfs macresources numpy pillow capstone
```

## Порядок
Рабочая папка по умолчанию — `work/` в корне репозитория (её можно задать переменной `BC_WORK`).

1. `extract/extract_hfs.py <образ.dsk> work/bcextract/vol` — файлы тома HFS (данные и ресурсы).
2. `extract/disasm.py` — дизассемблирование сегментов CODE (capstone) с именами MacsBug.
3. `extract/extract_all.py extract work/bcextract` — разбор всех блоков ALLCANM в `chunks.pkl` (парсеры — `extract/bcfmt.py`).
4. `extract/export_pack.py work/bcextract <игра>/original` — позы, циклы ходьбы, доска, заставка, звуки, `manifest.json`.
5. `extract/combat_export.py work/bcextract <игра>/original/combat` — 35 сцен поединков (симуляция `HANDLECOMBAT` + `CHECKTIME`).
6. `research/make_movement.py`, `extract/move_export.py` — данные движения и последовательности ходьбы/поворотов (`original/move`).
7. `research/recipes.py`, `research/make_recipes_json.py`, `extract/recipe_export.py` — дополнительные варианты боёв
   из последовательностей тех же боевых блоков.
8. `extract/ui_export.py work/bcextract/vol <игра>/original/ui` — логотип и картинки диалога превращения (PICT 400..403).

Папку `original` положите рядом с `BattleChess.exe` — игра найдёт её сама.

**Не распространяйте** полученные файлы: это материалы Interplay, только для личного использования.
