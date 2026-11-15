-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- v2: sorting (NULL = newest first) and the shipment's cost of goods. The count is unchanged (v1).
DROP FUNCTION IF EXISTS inventory.search_shipments(bigint, integer, integer);

CREATE OR REPLACE FUNCTION inventory.search_shipments(p_sales_order_id bigint, p_sort text, p_desc boolean,
    p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, number text, sales_order_id bigint, order_number text, shipped_by bigint,
               shipped_utc timestamptz, line_count integer, cost_of_goods numeric)
LANGUAGE sql STABLE
AS $$
    WITH s AS (
        SELECT s.id, s.number::text AS number, s.sales_order_id, s.order_number::text AS order_number, s.shipped_by, s.shipped_utc,
               (SELECT COUNT(*)::integer FROM inventory.shipment_line l WHERE l.shipment_id = s.id) AS line_count,
               (SELECT COALESCE(SUM(ROUND(l.quantity * l.unit_cost, 2)), 0) FROM inventory.shipment_line l WHERE l.shipment_id = s.id) AS cost_of_goods
        FROM inventory.shipment s
        WHERE (p_sales_order_id IS NULL OR s.sales_order_id = p_sales_order_id)
    )
    SELECT * FROM s
    ORDER BY
        CASE WHEN p_sort = 'number' AND NOT p_desc THEN s.number END ASC,
        CASE WHEN p_sort = 'number' AND p_desc     THEN s.number END DESC,
        CASE WHEN p_sort = 'orderNumber' AND NOT p_desc THEN s.order_number END ASC,
        CASE WHEN p_sort = 'orderNumber' AND p_desc     THEN s.order_number END DESC,
        CASE WHEN p_sort = 'costOfGoods' AND NOT p_desc THEN s.cost_of_goods END ASC,
        CASE WHEN p_sort = 'costOfGoods' AND p_desc     THEN s.cost_of_goods END DESC,
        CASE WHEN p_sort = 'shippedUtc' AND NOT p_desc THEN s.shipped_utc END ASC,
        s.shipped_utc DESC, s.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;
