-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- p_created_by: NULL = any order; otherwise only that user's (a rep sees only their own).
CREATE OR REPLACE FUNCTION sales.get_sales_order(p_order_id bigint, p_created_by bigint)
RETURNS TABLE (id bigint, order_number text, customer_id bigint, customer_name text, status integer, total numeric,
               created_by bigint, created_utc timestamptz, submitted_utc timestamptz, approved_by bigint,
               approved_utc timestamptz, rejected_by bigint, rejection_reason text, shipped_utc timestamptz,
               gross_total numeric, special_discount_program text, special_discount_id_label text,
               special_discount_holder_name text, special_discount_id_number text, special_discount_rate numeric,
               special_discount_vat_rate_removed numeric, special_discount_vat_exemption numeric, special_discount_amount numeric)
LANGUAGE sql STABLE
AS $$
    WITH gross AS (
        SELECT COALESCE(SUM(ROUND(l.quantity * l.unit_price, 2)), 0) AS total
        FROM sales.sales_order_line l WHERE l.sales_order_id = p_order_id
    )
    SELECT o.id, o.order_number::text, o.customer_id, c.name::text, o.status,
           g.total - o.special_discount_vat_exemption - o.special_discount_amount,
           o.created_by, o.created_utc, o.submitted_utc, o.approved_by, o.approved_utc, o.rejected_by,
           o.rejection_reason::text, o.shipped_utc,
           g.total, o.special_discount_program::text, o.special_discount_id_label::text,
           o.special_discount_holder_name::text, o.special_discount_id_number::text, o.special_discount_rate,
           o.special_discount_vat_rate_removed, o.special_discount_vat_exemption, o.special_discount_amount
    FROM sales.sales_order o
    CROSS JOIN gross g
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE o.id = p_order_id
      AND (p_created_by IS NULL OR o.created_by = p_created_by);
$$;

CREATE OR REPLACE FUNCTION sales.get_sales_order_lines(p_order_id bigint)
RETURNS TABLE (item_id bigint, item_name text, unit text, quantity numeric, unit_price numeric, special_discount_eligible boolean)
LANGUAGE sql STABLE
AS $$
    SELECT l.item_id, l.item_name::text, l.unit::text, l.quantity, l.unit_price, l.special_discount_eligible
    FROM sales.sales_order_line l
    WHERE l.sales_order_id = p_order_id
    ORDER BY l.id;
$$;
