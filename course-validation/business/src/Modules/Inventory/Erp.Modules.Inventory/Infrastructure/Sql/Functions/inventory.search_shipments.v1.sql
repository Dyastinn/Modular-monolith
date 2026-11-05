-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION inventory.search_shipments(p_sales_order_id bigint, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, number text, sales_order_id bigint, order_number text, shipped_by bigint,
               shipped_utc timestamptz, line_count integer)
LANGUAGE sql STABLE
AS $$
    SELECT s.id, s.number::text, s.sales_order_id, s.order_number::text, s.shipped_by, s.shipped_utc,
           (SELECT COUNT(*)::integer FROM inventory.shipment_line l WHERE l.shipment_id = s.id)
    FROM inventory.shipment s
    WHERE (p_sales_order_id IS NULL OR s.sales_order_id = p_sales_order_id)
    ORDER BY s.shipped_utc DESC, s.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION inventory.count_shipments(p_sales_order_id bigint)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM inventory.shipment s WHERE p_sales_order_id IS NULL OR s.sales_order_id = p_sales_order_id;
$$;
