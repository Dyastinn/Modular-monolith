-- Read side: one order's lines with item names, in the order they were added.
-- Shipped by migration AddReadFunctions. NEVER edit this file once released — write a .v2 and a new migration.
CREATE OR REPLACE FUNCTION sales.get_sales_order_lines(p_order_id bigint)
RETURNS TABLE (item_id bigint, item_name text, quantity integer, unit_price numeric)
LANGUAGE sql STABLE
AS $$
    SELECT l.item_id, i.name::text, l.quantity, l.unit_price
    FROM sales.sales_order_line l
    JOIN sales.item i ON i.id = l.item_id
    WHERE l.sales_order_id = p_order_id
    ORDER BY l.id;
$$;
