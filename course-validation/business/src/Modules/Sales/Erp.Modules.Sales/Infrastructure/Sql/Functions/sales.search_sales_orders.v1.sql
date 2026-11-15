-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- p_created_by: NULL = everyone's orders; otherwise only that user's (a rep sees only their own).
CREATE OR REPLACE FUNCTION sales.search_sales_orders(
    p_customer_id bigint, p_status integer, p_created_by bigint, p_search text, p_awaiting integer, p_exclude_creator bigint,
    p_sort text, p_desc boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, order_number text, customer_id bigint, customer_name text, status integer, total numeric,
               created_by bigint, created_utc timestamptz, submitted_utc timestamptz, pending_approvals integer)
LANGUAGE sql STABLE
AS $$
    WITH o AS (
        SELECT o.id, o.order_number::text AS order_number, o.customer_id, c.name::text AS customer_name, o.status,
               COALESCE((SELECT SUM(ROUND(l.quantity * l.unit_price, 2))
                         FROM sales.sales_order_line l WHERE l.sales_order_id = o.id), 0)
                 - o.special_discount_vat_exemption - o.special_discount_amount AS total,
               o.created_by, o.created_utc, o.submitted_utc, o.pending_approvals
        FROM sales.sales_order o
        JOIN sales.customer c ON c.id = o.customer_id
        WHERE (p_customer_id     IS NULL OR o.customer_id = p_customer_id)
          AND (p_status          IS NULL OR o.status      = p_status)
          AND (p_created_by      IS NULL OR o.created_by  = p_created_by)
          AND (p_awaiting        IS NULL OR (o.status = 1 AND (o.pending_approvals & p_awaiting) <> 0))
          AND (p_exclude_creator IS NULL OR o.created_by <> p_exclude_creator)
          AND (p_search          IS NULL OR o.order_number ILIKE '%' || p_search || '%' OR c.name ILIKE '%' || p_search || '%')
    )
    SELECT * FROM o
    -- One CASE per column and direction: each keeps its own type, and nothing is built from text.
    ORDER BY
        CASE WHEN p_sort = 'orderNumber'  AND NOT p_desc THEN o.order_number  END ASC,
        CASE WHEN p_sort = 'orderNumber'  AND p_desc     THEN o.order_number  END DESC,
        CASE WHEN p_sort = 'customerName' AND NOT p_desc THEN o.customer_name END ASC,
        CASE WHEN p_sort = 'customerName' AND p_desc     THEN o.customer_name END DESC,
        CASE WHEN p_sort = 'status'       AND NOT p_desc THEN o.status        END ASC,
        CASE WHEN p_sort = 'status'       AND p_desc     THEN o.status        END DESC,
        CASE WHEN p_sort = 'total'        AND NOT p_desc THEN o.total         END ASC,
        CASE WHEN p_sort = 'total'        AND p_desc     THEN o.total         END DESC,
        CASE WHEN p_sort = 'submittedUtc' AND NOT p_desc THEN o.submitted_utc END ASC,
        CASE WHEN p_sort = 'submittedUtc' AND p_desc     THEN o.submitted_utc END DESC,
        CASE WHEN p_sort = 'createdUtc'   AND NOT p_desc THEN o.created_utc   END ASC,
        o.created_utc DESC, o.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

-- Separate from the search so the count is right even for a page past the end.
CREATE OR REPLACE FUNCTION sales.count_sales_orders(
    p_customer_id bigint, p_status integer, p_created_by bigint, p_search text, p_awaiting integer, p_exclude_creator bigint)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*)
    FROM sales.sales_order o
    JOIN sales.customer c ON c.id = o.customer_id
    WHERE (p_customer_id     IS NULL OR o.customer_id = p_customer_id)
      AND (p_status          IS NULL OR o.status      = p_status)
      AND (p_created_by      IS NULL OR o.created_by  = p_created_by)
      AND (p_awaiting        IS NULL OR (o.status = 1 AND (o.pending_approvals & p_awaiting) <> 0))
      AND (p_exclude_creator IS NULL OR o.created_by <> p_exclude_creator)
      AND (p_search          IS NULL OR o.order_number ILIKE '%' || p_search || '%' OR c.name ILIKE '%' || p_search || '%');
$$;
