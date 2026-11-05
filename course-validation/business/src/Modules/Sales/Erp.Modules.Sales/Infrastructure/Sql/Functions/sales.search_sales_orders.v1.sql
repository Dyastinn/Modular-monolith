-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- p_created_by: NULL = everyone's orders; otherwise only that user's (a rep sees only their own).
CREATE OR REPLACE FUNCTION sales.search_sales_orders(
    p_customer_id bigint, p_status integer, p_created_by bigint, p_search text, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, order_number text, customer_id bigint, customer_name text, status integer, total numeric,
               created_by bigint, created_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT o.id, o.order_number::text, o.customer_id, c.name::text, o.status,
           COALESCE((SELECT SUM(ROUND(l.quantity * l.unit_price, 2))
                     FROM sales.sales_order_line l WHERE l.sales_order_id = o.id), 0),
           o.created_by, o.created_utc
    FROM sales.sales_order o
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE (p_customer_id IS NULL OR o.customer_id = p_customer_id)
      AND (p_status      IS NULL OR o.status      = p_status)
      AND (p_created_by  IS NULL OR o.created_by  = p_created_by)
      AND (p_search      IS NULL OR o.order_number ILIKE '%' || p_search || '%' OR c.name ILIKE '%' || p_search || '%')
    ORDER BY o.created_utc DESC, o.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

-- Separate from the search so the count is right even for a page past the end.
CREATE OR REPLACE FUNCTION sales.count_sales_orders(p_customer_id bigint, p_status integer, p_created_by bigint, p_search text)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*)
    FROM sales.sales_order o
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE (p_customer_id IS NULL OR o.customer_id = p_customer_id)
      AND (p_status      IS NULL OR o.status      = p_status)
      AND (p_created_by  IS NULL OR o.created_by  = p_created_by)
      AND (p_search      IS NULL OR o.order_number ILIKE '%' || p_search || '%' OR c.name ILIKE '%' || p_search || '%');
$$;
