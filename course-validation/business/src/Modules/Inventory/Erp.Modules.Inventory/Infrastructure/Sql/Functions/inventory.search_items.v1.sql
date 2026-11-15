-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION inventory.search_items(p_search text, p_active_only boolean, p_sort text, p_desc boolean,
    p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, sku text, name text, unit text, allows_fractions boolean, is_active boolean,
               on_hand numeric, reserved numeric, available numeric, average_cost numeric, stock_value numeric)
LANGUAGE sql STABLE
AS $$
    WITH i AS (
        SELECT i.id, i.sku::text AS sku, i.name::text AS name, i.unit::text AS unit, i.allows_fractions, i.is_active,
               i.on_hand, i.reserved, i.on_hand - i.reserved AS available, i.average_cost,
               ROUND(i.on_hand * i.average_cost, 2) AS stock_value
        FROM inventory.item i
        WHERE (p_search IS NULL OR i.sku ILIKE '%' || p_search || '%' OR i.name ILIKE '%' || p_search || '%')
          AND (NOT p_active_only OR i.is_active)
    )
    SELECT * FROM i
    ORDER BY
        CASE WHEN p_sort = 'sku' AND NOT p_desc THEN i.sku END ASC,
        CASE WHEN p_sort = 'sku' AND p_desc     THEN i.sku END DESC,
        CASE WHEN p_sort = 'onHand' AND NOT p_desc THEN i.on_hand END ASC,
        CASE WHEN p_sort = 'onHand' AND p_desc     THEN i.on_hand END DESC,
        CASE WHEN p_sort = 'reserved' AND NOT p_desc THEN i.reserved END ASC,
        CASE WHEN p_sort = 'reserved' AND p_desc     THEN i.reserved END DESC,
        CASE WHEN p_sort = 'available' AND NOT p_desc THEN i.available END ASC,
        CASE WHEN p_sort = 'available' AND p_desc     THEN i.available END DESC,
        CASE WHEN p_sort = 'averageCost' AND NOT p_desc THEN i.average_cost END ASC,
        CASE WHEN p_sort = 'averageCost' AND p_desc     THEN i.average_cost END DESC,
        CASE WHEN p_sort = 'stockValue' AND NOT p_desc THEN i.stock_value END ASC,
        CASE WHEN p_sort = 'stockValue' AND p_desc     THEN i.stock_value END DESC,
        CASE WHEN p_sort = 'name' AND p_desc THEN i.name END DESC,
        i.name, i.id
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

CREATE OR REPLACE FUNCTION inventory.stock_valuation()
RETURNS TABLE (item_count bigint, total_value numeric)
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FILTER (WHERE i.on_hand > 0), COALESCE(SUM(ROUND(i.on_hand * i.average_cost, 2)), 0)
    FROM inventory.item i;
$$;
