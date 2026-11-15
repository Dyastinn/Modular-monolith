-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- v2: how much of each line has shipped (an order may ship in parts). Split out of sales.get_sales_order.v1.sql.
DROP FUNCTION IF EXISTS sales.get_sales_order_lines(bigint);

CREATE OR REPLACE FUNCTION sales.get_sales_order_lines(p_order_id bigint)
RETURNS TABLE (item_id bigint, item_name text, unit text, quantity numeric, unit_price numeric, special_discount_eligible boolean,
               shipped_quantity numeric)
LANGUAGE sql STABLE
AS $$
    SELECT l.item_id, l.item_name::text, l.unit::text, l.quantity, l.unit_price, l.special_discount_eligible, l.shipped_quantity
    FROM sales.sales_order_line l
    WHERE l.sales_order_id = p_order_id
    ORDER BY l.id;
$$;
