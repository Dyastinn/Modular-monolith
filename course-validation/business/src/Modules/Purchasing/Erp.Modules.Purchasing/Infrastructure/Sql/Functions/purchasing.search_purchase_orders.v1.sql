-- Released scripts are never edited: change a function with a .v2 file and a new migration.
CREATE OR REPLACE FUNCTION purchasing.search_purchase_orders(p_supplier_id bigint, p_status integer, p_search text,
    p_awaiting integer, p_exclude_creator bigint, p_sort text, p_desc boolean, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, number text, supplier_id bigint, supplier_name text, status integer, total numeric,
               expected_date date, created_by bigint, created_utc timestamptz, submitted_utc timestamptz)
LANGUAGE sql STABLE
AS $$
    WITH po AS (
        SELECT po.id, po.number::text AS number, po.supplier_id, s.name::text AS supplier_name, po.status,
               COALESCE((SELECT SUM(ROUND(l.quantity * l.unit_cost, 2))
                         FROM purchasing.purchase_order_line l WHERE l.purchase_order_id = po.id), 0) AS total,
               po.expected_date, po.created_by, po.created_utc, po.submitted_utc
        FROM purchasing.purchase_order po
        JOIN purchasing.supplier s ON s.id = po.supplier_id
        WHERE (p_supplier_id     IS NULL OR po.supplier_id = p_supplier_id)
          AND (p_status          IS NULL OR po.status      = p_status)
          AND (p_awaiting        IS NULL OR (po.status = 7 AND p_awaiting = 1))
          AND (p_exclude_creator IS NULL OR po.created_by <> p_exclude_creator)
          AND (p_search          IS NULL OR po.number ILIKE '%' || p_search || '%' OR s.name ILIKE '%' || p_search || '%')
    )
    SELECT * FROM po
    ORDER BY
        CASE WHEN p_sort = 'number'       AND NOT p_desc THEN po.number        END ASC,
        CASE WHEN p_sort = 'number'       AND p_desc     THEN po.number        END DESC,
        CASE WHEN p_sort = 'supplierName' AND NOT p_desc THEN po.supplier_name END ASC,
        CASE WHEN p_sort = 'supplierName' AND p_desc     THEN po.supplier_name END DESC,
        CASE WHEN p_sort = 'status'       AND NOT p_desc THEN po.status        END ASC,
        CASE WHEN p_sort = 'status'       AND p_desc     THEN po.status        END DESC,
        CASE WHEN p_sort = 'total'        AND NOT p_desc THEN po.total         END ASC,
        CASE WHEN p_sort = 'total'        AND p_desc     THEN po.total         END DESC,
        CASE WHEN p_sort = 'expectedDate' AND NOT p_desc THEN po.expected_date END ASC,
        CASE WHEN p_sort = 'expectedDate' AND p_desc     THEN po.expected_date END DESC,
        CASE WHEN p_sort = 'submittedUtc' AND NOT p_desc THEN po.submitted_utc END ASC,
        CASE WHEN p_sort = 'submittedUtc' AND p_desc     THEN po.submitted_utc END DESC,
        CASE WHEN p_sort = 'createdUtc'   AND NOT p_desc THEN po.created_utc   END ASC,
        po.created_utc DESC, po.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION purchasing.count_purchase_orders(p_supplier_id bigint, p_status integer, p_search text,
    p_awaiting integer, p_exclude_creator bigint)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*)
    FROM purchasing.purchase_order po
    JOIN purchasing.supplier s ON s.id = po.supplier_id
    WHERE (p_supplier_id     IS NULL OR po.supplier_id = p_supplier_id)
      AND (p_status          IS NULL OR po.status      = p_status)
      AND (p_awaiting        IS NULL OR (po.status = 7 AND p_awaiting = 1))
      AND (p_exclude_creator IS NULL OR po.created_by <> p_exclude_creator)
      AND (p_search          IS NULL OR po.number ILIKE '%' || p_search || '%' OR s.name ILIKE '%' || p_search || '%');
$$;

CREATE OR REPLACE FUNCTION purchasing.get_purchase_order(p_id bigint)
RETURNS TABLE (id bigint, number text, supplier_id bigint, supplier_name text, status integer, total numeric,
               expected_date date, notes text, created_by bigint, created_utc timestamptz, approved_by bigint,
               approved_utc timestamptz, sent_utc timestamptz, submitted_utc timestamptz, rejected_by bigint,
               rejected_utc timestamptz, rejection_reason text)
LANGUAGE sql STABLE
AS $$
    SELECT po.id, po.number::text, po.supplier_id, s.name::text, po.status,
           COALESCE((SELECT SUM(ROUND(l.quantity * l.unit_cost, 2))
                     FROM purchasing.purchase_order_line l WHERE l.purchase_order_id = po.id), 0),
           po.expected_date, po.notes::text, po.created_by, po.created_utc, po.approved_by, po.approved_utc, po.sent_utc,
           po.submitted_utc, po.rejected_by, po.rejected_utc, po.rejection_reason::text
    FROM purchasing.purchase_order po
    JOIN purchasing.supplier s ON s.id = po.supplier_id
    WHERE po.id = p_id;
$$;

CREATE OR REPLACE FUNCTION purchasing.get_purchase_order_lines(p_id bigint)
RETURNS TABLE (id bigint, item_id bigint, sku text, item_name text, unit text, quantity numeric,
               received_quantity numeric, unit_cost numeric)
LANGUAGE sql STABLE
AS $$
    SELECT l.id, l.item_id, l.sku::text, l.item_name::text, l.unit::text, l.quantity, l.received_quantity, l.unit_cost
    FROM purchasing.purchase_order_line l
    WHERE l.purchase_order_id = p_id
    ORDER BY l.id;
$$;
