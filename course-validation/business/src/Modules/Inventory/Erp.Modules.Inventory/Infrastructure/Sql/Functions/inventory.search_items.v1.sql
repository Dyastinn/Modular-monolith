-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION inventory.search_items(p_search text, p_active_only boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, sku text, name text, unit text, allows_fractions boolean, is_active boolean,
               on_hand numeric, reserved numeric)
LANGUAGE sql STABLE
AS $$
    SELECT i.id, i.sku::text, i.name::text, i.unit::text, i.allows_fractions, i.is_active, i.on_hand, i.reserved
    FROM inventory.item i
    WHERE (p_search IS NULL OR i.sku ILIKE '%' || p_search || '%' OR i.name ILIKE '%' || p_search || '%')
      AND (NOT p_active_only OR i.is_active)
    ORDER BY i.name, i.id
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION inventory.count_items(p_search text, p_active_only boolean)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*)
    FROM inventory.item i
    WHERE (p_search IS NULL OR i.sku ILIKE '%' || p_search || '%' OR i.name ILIKE '%' || p_search || '%')
      AND (NOT p_active_only OR i.is_active);
$$;
