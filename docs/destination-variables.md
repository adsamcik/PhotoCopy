# Destination Variables and Fallback Syntax

PhotoCopy uses a powerful variable substitution system in destination paths to organize your photos automatically. This document covers all available variables and the fallback syntax for handling missing values.

## Available Variables

### Date/Time Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `{year}` | Year from file date/EXIF | `2024` |
| `{month}` | Month (01-12) | `03` |
| `{day}` | Day of month (01-31) | `15` |
| `{dayOfYear}` | Day of year (1-365) | `74` |

### File Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `{name}` | Full filename with extension | `photo.jpg` |
| `{namenoext}` | Filename without extension | `photo` |
| `{ext}` | File extension | `jpg` |
| `{directory}` | Original relative directory | `vacation/day1` |
| `{number}` | Duplicate counter | `1`, `2`, `3` |

### Location Variables

These require photos with GPS metadata:

| Variable | Description | Example |
|----------|-------------|---------|
| `{district}` | Neighborhood/district name | `Troja` |
| `{city}` | City name | `Prague` |
| `{county}` | County/region name | `Prague` |
| `{state}` | State/province name | `Bohemia` |
| `{country}` | Country name or code | `CZ` or `Czechia` |

---

## Fallback Syntax

When a variable value is unavailable (e.g., no GPS data), PhotoCopy provides flexible fallback options.

### Global Fallback

Set a default in `appsettings.yaml`:

```yaml
photoCopy:
  unknownLocationFallback: "Unknown"
```

### Inline Literal Fallback

Override the global fallback with a literal string:

```
{city|NoLocation}    → "Prague" or "NoLocation"
{country|Somewhere}  → "US" or "Somewhere"
```

### Variable Chains

Try multiple variables in sequence. The first non-empty value is used:

```
{city|country}                → Try city, then country
{district|city|country}       → Three-level chain
{city|country|Unknown}        → Chain ending with literal
```

### Empty Fallback (Folder Omission)

Use an empty fallback to omit the folder level entirely:

```
{city|}    → "Prague" or (nothing - folder omitted)
```

This is useful for optional folder levels:

```
Destination: /photos/{year}/{city|}/{month}/{name}

With city:    /photos/2024/Prague/03/photo.jpg
Without city: /photos/2024/03/photo.jpg
```

---

## Conditional Variables (Threshold-Based)

Use location values only when you have enough photos to justify a separate folder.

### Syntax

```
{variable?condition|fallback}
```

### Available Conditions

| Condition | Syntax | Description |
|-----------|--------|-------------|
| Minimum | `?min=N` | Use variable only if ≥N photos match |
| Maximum | `?max=N` | Use variable only if ≤N photos match |
| Range | `?min=N,max=M` | Use variable only if N ≤ photos ≤ M |

### Examples

```bash
# City folder only if ≥10 photos from that city
{city?min=10|country}

# City folder only if ≤100 photos (avoid huge folders)
{city?max=100|country}

# City folder if between 10-100 photos
{city?min=10,max=100|country}

# Chained conditions
{city?min=10|country?min=5|Misc}
```

### How Thresholds Work

PhotoCopy uses a two-pass architecture:

1. __First Pass (Scan):__ Collects statistics on all files, counting photos per location value
2. __Second Pass (Copy/Move):__ Applies threshold conditions using the collected statistics

### Example Scenario

```
Source files:
  - 15 photos in Prague
  - 3 photos in Brno  
  - 8 photos in Vienna

Pattern: {country}/{city?min=10}/{name}

Results:
  - Prague → CZ/Prague/photo.jpg    (15 ≥ 10, uses city)
  - Brno   → CZ/photo.jpg           (3 < 10, city omitted)
  - Vienna → AT/photo.jpg           (8 < 10, city omitted)
```

---

## Combining Features

All fallback features can be combined:

```bash
# Full example with conditions and chains
/photos/{year}/{district|city?min=10|country|Unknown}/{month}/{name}
```

Resolution order:
1. Try `district` (no conditions)
2. If empty → try `city` (only if ≥10 photos)
3. If `city` empty or below threshold → try `country`
4. If all fail → use literal "Unknown"

---

## Variable Detection Rules

PhotoCopy distinguishes between variables and literal text in fallback chains:

| Token | Treatment | Reason |
|-------|-----------|--------|
| `city` | Variable | Known variable name |
| `country` | Variable | Known variable name |
| `Unknown` | Literal | Not a known variable |
| `NoLocation` | Literal | Not a known variable |

__Known variables:__ `year`, `month`, `day`, `name`, `namenoext`, `ext`, `directory`, `number`, `district`, `city`, `county`, `state`, `country`

---

## Best Practices

1. __Always end chains with a guaranteed fallback:__
   ```
   {city|country|Unknown}  ✓
   {city|country}          △ May result in empty path
   ```

2. __Use thresholds to prevent folder fragmentation:__
   ```
   {city?min=5|country}    # Consolidates small city groups
   ```

3. __Use empty fallbacks for optional folder levels:__
   ```
   {city|}/{year}          # City folder only when available
   ```

4. __Consider your photo collection size:__
   - Small collections (< 1000): Lower thresholds (`min=3`)
   - Large collections (> 10000): Higher thresholds (`min=20`)
