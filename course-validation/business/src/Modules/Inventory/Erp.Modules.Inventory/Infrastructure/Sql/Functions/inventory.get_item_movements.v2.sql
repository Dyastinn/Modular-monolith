-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- v2: what each unit was worth as it moved, and the movement's value (+ in, - out). The count is unchanged (v1).
DROP FUNCTION IF EXISTS inventory.get_item_movements(bigint, integer, integer);

CREATE OR REPLACE FUNCTION inventory.get_item_movements(p_item_id bigint, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, quantity numeric, kind integer, reference text, reason text, user_id bigint, occurred_utc timestamptz,
               unit_cost numeric, value numeric)
LANGUAGE sql STABLE
AS $$
    SELECT m.id, m.quantity, m.kind, m.reference::text, m.reason::text, m.user_id, m.occurred_utc,
           m.unit_cost, ROUND(m.quantity * m.unit_cost, 2)
    FROM inventory.stock_movement m
    WHERE m.item_id = p_item_id
    ORDER BY m.occurred_utc DESC, m.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;
