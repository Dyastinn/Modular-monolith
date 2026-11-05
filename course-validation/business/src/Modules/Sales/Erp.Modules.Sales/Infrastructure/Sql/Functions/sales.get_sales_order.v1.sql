-- Read side: one order's header with its customer name and computed total. Pair with get_sales_order_lines.
-- Shipped by migration AddReadFunctions. NEVER edit this file once released — write a .v2 and a new migration.
CREATE OR REPLACE FUNCTION sales.get_sales_order(p_order_id bigint)
RETURNS TABLE (id bigint, order_number text, customer_id bigint, customer_name text,
               status integer, total numeric, created_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    SELECT o.id, o.order_number::text, o.customer_id, c.name::text, o.status,
           COALESCE((SELECT SUM(l.quantity * l.unit_price)
                     FROM sales.sales_order_line l WHERE l.sales_order_id = o.id), 0),
           o.created_utc
    FROM sales.sales_order o
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE o.id = p_order_id;
$$;
