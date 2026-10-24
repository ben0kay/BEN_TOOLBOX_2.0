# World map geometry

Made with Natural Earth. Public-domain Natural Earth 1:110m physical land polygons, version 4.1.0 (upstream ne_110m_land.VERSION.txt).

- Source: https://github.com/nvkelso/natural-earth-vector/blob/master/geojson/ne_110m_land.geojson
- Version: https://github.com/nvkelso/natural-earth-vector/blob/master/110m_physical/ne_110m_land.VERSION.txt
- Terms: https://www.naturalearthdata.com/about/terms-of-use/
- Retrieved: 2026-10-24
- Source SHA-256: `9e0729ee253ca7d7a5c4ae9395fb1902264c5377c52e224d13dd85010e2835d9`

world_110m.json contains 128 polygon rings: extract each Polygon/MultiPolygon ring from the source, retain longitude/latitude order and round coordinates to five decimals. The app constructs coastline geometry once and transforms it with the same equirectangular projection as earthquake pins. No map tiles or runtime geography download. This is a coarse world/regional outline, not street-level mapping or authoritative boundaries.
