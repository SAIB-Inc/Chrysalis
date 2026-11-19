use fixtures/order_book.{Order, Ticket} as order_book_types

validator order_book {
  spend(_datum: Option<Ticket>, _order: Order, _ref: Data, _tx: Data) { True }
  else(_) { fail }
}
