namespace Gley.UrbanSystem.Editor
{
    public class GridEditorData : EditorData
    {
        GridData _gridData;

        public GridEditorData()
        {
            LoadAllData();
        }


        public int GetGridCellSize()
        {
            if (_gridData.GridCellSize == 0)
            {
                return 50;
            }
            return _gridData.GridCellSize;
        }


        public RowData[] GetGrid()
        {
            return _gridData.Grid;
        }


        public override void LoadAllData()
        {
            _gridData = MonoBehaviourUtilities.GetOrCreateObjectScript<GridData>(UrbanSystemConstants.PlayHolder, false);
        }
    }
}