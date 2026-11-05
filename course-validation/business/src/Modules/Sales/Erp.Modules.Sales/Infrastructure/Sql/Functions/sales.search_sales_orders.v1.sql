-- Read side: one page of orders, newest first. Every row also carries total_count (all matches, ignoring the
-- page), so the caller gets the page and the count in one round trip.
-- Shipped by migration AddReadFunctions. NEVER edit this file once released — write a .v2 and a new migration.
CREATE OR REPLACE FUNCTION sales.search_sales_orders(
    p_customer_id bigint, p_status integer, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, order_number text, customer_name text, status integer,
               total numeric, created_utc timestamptz, total_count bigint)
LANGUAGE sql STABLE
AS $$
    SELECT o.id, o.order_number::text, c.name::text, o.status,
           COALESCE((SELECT SUM(l.quantity * l.unit_price)
                     FROM sales.sales_order_line l WHERE l.sales_order_id = o.id), 0),
           o.created_utc,
           COUNT(*) OVER ()
    FROM sales.sales_order o
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE (p_customer_id IS NULL OR o.customer_id = p_customer_id)
      AND (p_status      IS NULL OR o.status      = p_status)
    ORDER BY o.created_utc DESC, o.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;
