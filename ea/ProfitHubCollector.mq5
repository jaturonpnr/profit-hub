//+------------------------------------------------------------------+
//| ProfitHubCollector.mq5                                           |
//| Pushes closed deals to Profit Hub. Read-only; never trades.      |
//| Attach to ONE chart per terminal. Whitelist ApiUrl in            |
//| Tools > Options > Expert Advisors > Allow WebRequest.            |
//+------------------------------------------------------------------+
#property strict

input string ApiUrl        = "https://your-api.onrender.com"; // Profit Hub API base URL
input string IngestKey     = "";                               // per-account Ingest Key
input int    IntervalSec   = 120;                              // push every N seconds (1-5 min)
input int    BatchSize     = 100;                              // deals per HTTP request (max 1000)

string GV_LAST;   // global variable name persisting last pushed deal time
int    g_batch;   // effective batch size (clamped to backend limit)

int OnInit()
{
   if(StringLen(IngestKey) == 0) { Print("ProfitHub: IngestKey is empty"); return INIT_PARAMETERS_INCORRECT; }
   GV_LAST = "PH_LAST_" + (string)AccountInfoInteger(ACCOUNT_LOGIN);
   g_batch = (int)MathMax(1, MathMin(BatchSize, 1000)); // backend rejects >1000 deals per request
   EventSetTimer((int)MathMax(60, IntervalSec));
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason) { EventKillTimer(); }

void OnTimer()
{
   // Delta fetch: from last successfully pushed deal time (0 on first run = full backfill)
   datetime from = (datetime)(GlobalVariableCheck(GV_LAST) ? GlobalVariableGet(GV_LAST) : 0);
   if(!HistorySelect(from + 1, TimeCurrent())) return;

   int total = HistoryDealsTotal();

   // Pass 1: snapshot the deals we will send while the outer selection is
   // active. HistorySelectByPosition (pass 2) replaces the selection, so we
   // must not interleave it with indexed access to the outer list.
   ulong    tickets[];  long     posIds[];   string  typeStrs[];
   datetime times[];    string   symbols[];  double  lots[];
   double   closes[];   double   profits[];  double  commissions[];
   double   swaps[];    long     magics[];   string  comments[];
   ArrayResize(tickets, g_batch);  ArrayResize(posIds, g_batch);   ArrayResize(typeStrs, g_batch);
   ArrayResize(times, g_batch);    ArrayResize(symbols, g_batch);  ArrayResize(lots, g_batch);
   ArrayResize(closes, g_batch);   ArrayResize(profits, g_batch);  ArrayResize(commissions, g_batch);
   ArrayResize(swaps, g_batch);    ArrayResize(magics, g_batch);   ArrayResize(comments, g_batch);

   int count = 0;
   bool hitBatchLimit = false; // true when the loop was cut short by the batch cap
   datetime maxTime = from; // advances ONLY over deals included in this batch

   for(int i = 0; i < total; i++)
   {
      ulong ticket = HistoryDealGetTicket(i);
      if(ticket == 0) continue;
      long entry = HistoryDealGetInteger(ticket, DEAL_ENTRY);
      long type  = HistoryDealGetInteger(ticket, DEAL_TYPE);
      datetime t = (datetime)HistoryDealGetInteger(ticket, DEAL_TIME);

      string typeStr = "";
      if(type == DEAL_TYPE_BALANCE) typeStr = "balance";
      else if((type == DEAL_TYPE_BUY || type == DEAL_TYPE_SELL) &&
              (entry == DEAL_ENTRY_OUT || entry == DEAL_ENTRY_INOUT || entry == DEAL_ENTRY_OUT_BY))
         typeStr = (type == DEAL_TYPE_BUY) ? "sell" : "buy"; // a position closes with a deal in the OPPOSITE direction, so report the position's direction
      else continue; // skip entry-in deals; we record positions when they close

      tickets[count]     = ticket;
      posIds[count]      = HistoryDealGetInteger(ticket, DEAL_POSITION_ID);
      typeStrs[count]    = typeStr;
      times[count]       = t;
      symbols[count]     = HistoryDealGetString(ticket, DEAL_SYMBOL);
      lots[count]        = HistoryDealGetDouble(ticket, DEAL_VOLUME);
      closes[count]      = HistoryDealGetDouble(ticket, DEAL_PRICE);
      profits[count]     = HistoryDealGetDouble(ticket, DEAL_PROFIT);
      commissions[count] = HistoryDealGetDouble(ticket, DEAL_COMMISSION);
      swaps[count]       = HistoryDealGetDouble(ticket, DEAL_SWAP);
      magics[count]      = HistoryDealGetInteger(ticket, DEAL_MAGIC);
      comments[count]    = HistoryDealGetString(ticket, DEAL_COMMENT);

      if(t > maxTime) maxTime = t;
      count++;
      if(count >= g_batch) { hitBatchLimit = true; break; } // remaining deals go next timer tick; watermark stops at last INCLUDED deal
   }

   if(count == 0) return;

   // Pass 2: open price/time from each position's entry deal, then build JSON.
   string json = "";
   for(int j = 0; j < count; j++)
   {
      double openPrice = 0; datetime openTime = times[j];
      if(typeStrs[j] != "balance" && HistorySelectByPosition(posIds[j]))
      {
         int n = HistoryDealsTotal();
         for(int k = 0; k < n; k++)
         {
            ulong tk = HistoryDealGetTicket(k);
            if(HistoryDealGetInteger(tk, DEAL_ENTRY) == DEAL_ENTRY_IN)
            { openPrice = HistoryDealGetDouble(tk, DEAL_PRICE);
              openTime = (datetime)HistoryDealGetInteger(tk, DEAL_TIME); break; }
         }
      }

      if(j > 0) json += ",";
      json += StringFormat(
        "{\"dealTicket\":%I64u,\"positionId\":%I64d,\"symbol\":\"%s\",\"type\":\"%s\","
        "\"lots\":%.3f,\"openPrice\":%.5f,\"closePrice\":%.5f,"
        "\"openTimeUtc\":\"%s\",\"closeTimeUtc\":\"%s\","
        "\"grossProfit\":%.2f,\"commission\":%.2f,\"swap\":%.2f,"
        "\"magicNumber\":%I64d,\"comment\":\"%s\"}",
        tickets[j], posIds[j], EscapeJson(symbols[j]), typeStrs[j],
        lots[j], openPrice, closes[j],
        ToIsoUtc(openTime), ToIsoUtc(times[j]),
        profits[j], commissions[j], swaps[j],
        magics[j], EscapeJson(comments[j]));
   }

   if(Push("[" + json + "]"))
   {
      // When the batch limit was hit, persist maxTime-1 so the next cycle re-fetches
      // deals sharing the same second as the last included deal — there may be more of
      // them that were cut off. The backend deduplicates, so re-sending is safe.
      datetime watermark = hitBatchLimit ? maxTime - 1 : maxTime;
      GlobalVariableSet(GV_LAST, (double)watermark); // advance watermark only on success
   }
}

string ToIsoUtc(datetime serverTime)
{
   // Server time -> UTC using the broker's current GMT offset
   datetime utc = serverTime - (TimeTradeServer() - TimeGMT());
   string s = TimeToString(utc, TIME_DATE|TIME_SECONDS); // "2026.05.28 03:06:00"
   StringReplace(s, ".", "-"); StringReplace(s, " ", "T");
   return s + "Z"; // ISO 8601: "2026-05-28T03:06:00Z"
}

string EscapeJson(string s)
{
   string r = s;
   StringReplace(r, "\\", "\\\\");
   StringReplace(r, "\"", "\\\"");
   StringReplace(r, "\r", "\\r");
   StringReplace(r, "\n", "\\n");
   StringReplace(r, "\t", "\\t");
   return r;
}

bool Push(string dealsJson)
{
   string body = "{\"deals\":" + dealsJson + "}";
   char data[], result[];
   int len = StringToCharArray(body, data, 0, WHOLE_ARRAY, CP_UTF8) - 1; // UTF-8 bytes, drop trailing NUL
   if(len < 0) len = 0;
   ArrayResize(data, len);
   string headers = "Content-Type: application/json\r\nX-Ingest-Key: " + IngestKey + "\r\n";
   string respHeaders;
   ResetLastError();
   int status = WebRequest("POST", ApiUrl + "/api/ingest/deals", headers, 5000, data, result, respHeaders);
   if(status == 200) return true;
   PrintFormat("ProfitHub: push failed status=%d err=%d (will retry next cycle)", status, GetLastError());
   return false; // no retry loop; idempotency makes the next cycle safe (ADR 0001)
}
//+------------------------------------------------------------------+
