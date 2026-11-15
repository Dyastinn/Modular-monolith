-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- v2: the approvals still pending, and who released a credit hold. The _lines companion is unchanged (v1).
DROP FUNCTION IF EXISTS sales.get_sales_order(bigint, bigint);

CREATE OR REPLACE FUNCTION sales.get_sales_order(p_order_id bigint, p_created_by bigint)
RETURNS TABLE (id bigint, order_number text, customer_id bigint, customer_name text, status integer, total numeric,
               created_by bigint, created_utc timestamptz, submitted_utc timestamptz, approved_by bigint,
               approved_utc timestamptz, rejected_by bigint, rejection_reason text, shipped_utc timestamptz,
               gross_total numeric, special_discount_program text, special_discount_id_label text,
               special_discount_holder_name text, special_discount_id_number text, special_discount_rate numeric,
               special_discount_vat_rate_removed numeric, special_discount_vat_exemption numeric, special_discount_amount numeric,
               pending_approvals integer, credit_released_by bigint, credit_released_utc timestamptz)
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
           o.special_discount_vat_rate_removed, o.special_discount_vat_exemption, o.special_discount_amount,
           o.pending_approvals, o.credit_released_by, o.credit_released_utc
    FROM sales.sales_order o
    CROSS JOIN gross g
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE o.id = p_order_id
      AND (p_created_by IS NULL OR o.created_by = p_created_by);
$$;
